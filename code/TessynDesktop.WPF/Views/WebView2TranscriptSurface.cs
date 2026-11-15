using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Transcript;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <see cref="ITranscriptSurface"/> over the shared WebView2. The page is served from a virtual host mapped to the app's
/// <c>Web</c> folder under a strict Content-Security-Policy. The surface exposes no host objects and has one narrow channel:
/// JSON messages in both directions. Every navigation other than the page itself is cancelled, and clicks on http(s) links
/// are opened in the system browser (FR.26.3 interim). Other schemes, downloads, permission prompts and foreign requests
/// are refused.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class WebView2TranscriptSurface : ITranscriptSurface
{
	private const string HostName = "app.local";
	private static readonly string PageUri = $"https://{HostName}/{Constants.Paths.TranscriptPageFileName}";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private readonly WebViewEnvironmentProvider _environment;
	private readonly AppPaths _paths;
	private readonly IUrlLauncher _launcher;
	private readonly UrlPolicy _policy;
	private readonly MediaPreviewPolicy _media;
	private readonly ILogger<WebView2TranscriptSurface> _logger;
	private readonly List<string> _pending = new();

	/// <summary>https media URLs the user asked to load (click-to-load); cleared on every <see cref="Load"/>, like the page's own state.</summary>
	private readonly HashSet<string> _approvedMedia = new(StringComparer.Ordinal);

	private WebView2? _control;
	private CoreWebView2Environment? _env;
	private bool _ready;
	private bool _everReady;
	private int _token;
	private Task _sendTail = Task.CompletedTask;
	private int _generation;

	public WebView2TranscriptSurface(WebViewEnvironmentProvider environment, AppPaths paths, IUrlLauncher launcher, UrlPolicy policy, MediaPreviewPolicy media, ILogger<WebView2TranscriptSurface> logger)
	{
		_environment = environment;
		_paths = paths;
		_launcher = launcher;
		_policy = policy;
		_media = media;
		_logger = logger;
		_media.Changed += (_, _) => PostPolicy();
	}

	public event EventHandler<ViewAnchorEventArgs>? ViewChanged;

	public event EventHandler<PathClickedEventArgs>? PathClicked;

	public event EventHandler<TranscriptPaintedEventArgs>? Painted;

	public event EventHandler? OlderRequested;

	public event EventHandler<int>? ExpandRequested;

	public event EventHandler<string>? LinkClicked;

	public event EventHandler? FindRequested;

	public event EventHandler<int>? FindStepRequested;

	public event EventHandler<FindStatusEventArgs>? FindStatusChanged;

	public event EventHandler? PageRestarted;

	/// <summary>The WebView2 browser process died: the view must create a new control and call <see cref="AttachAsync"/> again.</summary>
	public event EventHandler? BrowserLost;

	/// <summary>Binds the surface to the WebView2 control of the session pane and starts loading the page.</summary>
	public async Task AttachAsync(WebView2 control)
	{
		_control = control;
		_ready = false;
		_pending.Clear();
		_generation++;
		_env = await _environment.GetAsync();
		await control.EnsureCoreWebView2Async(_env);
		var core = control.CoreWebView2;

		var settings = core.Settings;
		settings.AreHostObjectsAllowed = false;
		settings.AreDevToolsEnabled = false;
		settings.AreBrowserAcceleratorKeysEnabled = false;
		settings.IsStatusBarEnabled = false;
		settings.IsPasswordAutosaveEnabled = false;
		settings.IsGeneralAutofillEnabled = false;
		settings.IsSwipeNavigationEnabled = false;
		settings.IsBuiltInErrorPageEnabled = false;
		settings.IsWebMessageEnabled = true;
		settings.AreDefaultContextMenusEnabled = false;

		core.SetVirtualHostNameToFolderMapping(HostName, _paths.WebAssetsDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
		core.NavigationStarting += OnNavigationStarting;
		core.FrameNavigationStarting += (_, e) => e.Cancel = true;
		core.NewWindowRequested += OnNewWindowRequested;
		core.DownloadStarting += (_, e) => e.Cancel = true;
		core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
		core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
		core.WebResourceRequested += OnWebResourceRequested;
		core.WebMessageReceived += OnWebMessageReceived;
		core.ProcessFailed += OnProcessFailed;
		SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
		SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
		core.Navigate(PageUri);
	}

	public void Load(string key, IReadOnlyList<TranscriptEntryView> entries, bool hasOlder, bool toBottom)
	{
		_token++;
		_approvedMedia.Clear();
		var token = _token;
		var snapshot = entries.ToList();
		Post(() => JsonSerializer.Serialize(new { cmd = "load", tok = token, key, entries = snapshot.Select(ToPage), hasOlder, toBottom }, Json), replacesQueue: true);
	}

	public void Prepend(IReadOnlyList<TranscriptEntryView> entries, bool hasOlder)
	{
		var token = _token;
		var snapshot = entries.ToList();
		Post(() => JsonSerializer.Serialize(new { cmd = "prepend", tok = token, entries = snapshot.Select(ToPage), hasOlder }, Json));
	}

	public void Append(IReadOnlyList<TranscriptEntryView> entries)
	{
		var token = _token;
		var snapshot = entries.ToList();
		Post(() => JsonSerializer.Serialize(new { cmd = "append", tok = token, entries = snapshot.Select(ToPage) }, Json));
	}

	public void Replace(TranscriptEntryView entry)
	{
		var token = _token;
		Post(() => JsonSerializer.Serialize(new { cmd = "replace", tok = token, entry = ToPage(entry) }, Json));
	}

	public void Find(string term, IReadOnlyList<int> matchingIndexes)
	{
		var snapshot = matchingIndexes.ToList();
		Post(() => JsonSerializer.Serialize(new { cmd = "find", term, idx = snapshot }, Json));
	}

	public void FindStep(int direction) =>
		Post(() => JsonSerializer.Serialize(new { cmd = "findStep", dir = direction }, Json));

	public void ClearFind() => Post(() => JsonSerializer.Serialize(new { cmd = "clearFind" }, Json));

	public void GoToEntry(int index, string? quote, bool highlight) =>
		Post(() => JsonSerializer.Serialize(new { cmd = "goto", i = index, quote, hl = highlight }, Json));

	private static object ToPage(TranscriptEntryView view) =>
		new { i = view.Index, r = view.Role, ts = view.Timestamp, html = view.Html, trunc = view.Truncated, full = view.FullLength, h = view.EstimatedHeight, m = view.Media ?? [], p = view.Paths ?? [] };

	/// <summary>Hands the media allowlist to the page, which decides auto-load versus click-to-load from it.</summary>
	private void PostPolicy()
	{
		if (_control?.CoreWebView2 == null || !_ready)
			return;

		_control.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { cmd = "policy", hosts = _media.Hosts }, Json));
	}

	/// <summary>
	/// Queues a message. The JSON is built on a thread-pool thread (a warm switch can carry thousands of entries), and the messages
	/// are posted to the page on the UI thread in the order they were queued.
	/// </summary>
	private void Post(Func<string> build, bool replacesQueue = false)
	{
		var generation = _generation;
		var previous = _sendTail;
		var built = Task.Run(build);
		_sendTail = PostInOrderAsync(previous, built, generation, replacesQueue);
	}

	private async Task PostInOrderAsync(Task previous, Task<string> built, int generation, bool replacesQueue)
	{
		await previous.ConfigureAwait(true);
		string json;
		try
		{
			json = await built.ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Building a page message failed");
			return;
		}

		if (generation != _generation)
			return;

		if (!_ready || _control?.CoreWebView2 == null)
		{
			if (replacesQueue)
				_pending.Clear();
			_pending.Add(json);
			return;
		}

		_control.CoreWebView2.PostWebMessageAsJson(json);
	}

	private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
	{
		_logger.LogWarning("WebView2 process failed: {Kind}", e.ProcessFailedKind);
		_ready = false;
		switch (e.ProcessFailedKind)
		{
			case CoreWebView2ProcessFailedKind.BrowserProcessExited:
				BrowserLost?.Invoke(this, EventArgs.Empty);
				break;
			case CoreWebView2ProcessFailedKind.RenderProcessExited:
			case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
				_control?.CoreWebView2?.Reload();
				break;
		}
	}

	private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
	{
		if (e.Uri == PageUri)
			return;

		e.Cancel = true;
		if (e.IsUserInitiated)
			_launcher.TryOpen(e.Uri);
		else
			_logger.LogInformation("Blocked a script-initiated navigation");
	}

	private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
	{
		e.Handled = true;
		_launcher.TryOpen(e.Uri);
	}

	private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
	{
		if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri))
		{
			if (uri.Host == HostName)
				return;

			// The only foreign requests the page may make are https images and video the user approved or the allowlist covers (FR.26.4).
			var isMedia = e.ResourceContext is CoreWebView2WebResourceContext.Image or CoreWebView2WebResourceContext.Media;
			if (isMedia && uri.Scheme == Uri.UriSchemeHttps && (_approvedMedia.Contains(uri.AbsoluteUri) || _media.IsAllowedHost(uri.Host)))
				return;
		}

		_logger.LogInformation("Blocked a request to a foreign host");
		e.Response = _env!.CreateWebResourceResponse(null, 403, "Blocked", string.Empty);
	}

	private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
	{
		if (e.Source != PageUri)
			return;

		try
		{
			using var document = JsonDocument.Parse(e.WebMessageAsJson);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ev", out var evProperty) || evProperty.ValueKind != JsonValueKind.String)
				return;

			switch (evProperty.GetString())
			{
				case "ready":
					FlushPending();
					break;
				case "painted" when TokenMatches(root):
					Painted?.Invoke(this, new TranscriptPaintedEventArgs(root.GetProperty("key").GetString() ?? string.Empty, root.GetProperty("jsMs").GetDouble()));
					break;
				case "needOlder" when TokenMatches(root):
					OlderRequested?.Invoke(this, EventArgs.Empty);
					break;
				case "expand" when TokenMatches(root):
					ExpandRequested?.Invoke(this, root.GetProperty("i").GetInt32());
					break;
				case "link":
					if (root.GetProperty("href").GetString() is { } href && _policy.IsAllowed(href))
						LinkClicked?.Invoke(this, href);
					break;
				case "findRequested":
					FindRequested?.Invoke(this, EventArgs.Empty);
					break;
				case "findStep":
					FindStepRequested?.Invoke(this, root.GetProperty("dir").GetInt32() >= 0 ? 1 : -1);
					break;
				case "findStatus":
					FindStatusChanged?.Invoke(this, new FindStatusEventArgs(root.GetProperty("cur").GetInt32(), root.GetProperty("count").GetInt32()));
					break;
				case "view" when TokenMatches(root):
					ViewChanged?.Invoke(this, new ViewAnchorEventArgs(root.GetProperty("i").GetInt32(), root.GetProperty("bottom").GetBoolean()));
					break;
				case "path" when TokenMatches(root):
					if (root.GetProperty("path").GetString() is { Length: > 0 } path)
						PathClicked?.Invoke(this, new PathClickedEventArgs(root.GetProperty("i").GetInt32(), path));
					break;
				case "refHl" when TokenMatches(root):
					_logger.LogInformation("Reference jump: entry {Index} visible {Visible}, quote highlighted {Quote}, entry outlined {Entry}",
						root.GetProperty("i").GetInt32(), root.GetProperty("visible").GetBoolean(), root.GetProperty("quote").GetBoolean(), root.GetProperty("entry").GetBoolean());
					break;
				case "mediaLoad" when TokenMatches(root):
					ApproveMedia(root.GetProperty("url").GetString(), root.GetProperty("i").GetInt32(), root.GetProperty("j").GetInt32());
					break;
			}
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
		{
			_logger.LogWarning(ex, "Ignored a malformed page message");
		}
	}

	/// <summary>The user clicked "Load preview": remember the URL as approved, then tell the page it may create the element.</summary>
	private void ApproveMedia(string? url, int entry, int slot)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Host.Length == 0)
		{
			_logger.LogInformation("Ignored a media load request for an invalid URL");
			return;
		}

		_approvedMedia.Add(uri.AbsoluteUri);
		_logger.LogInformation("Media load approved by a click for host {Host}", uri.Host);
		_control?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { cmd = "mediaOk", tok = _token, i = entry, j = slot }, Json));
	}

	private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SystemParameters.WindowGlassColor))
			ApplyAccent();
	}

	/// <summary>Hands the Windows accent colour to the page as the --accent CSS variable; light/dark follows the system through prefers-color-scheme.</summary>
	private void ApplyAccent()
	{
		if (_control?.CoreWebView2 == null || !_ready)
			return;

		var accent = SystemParameters.WindowGlassColor;
		_ = _control.CoreWebView2.ExecuteScriptAsync($"document.documentElement.style.setProperty('--accent','#{accent.R:X2}{accent.G:X2}{accent.B:X2}')");
	}

	private bool TokenMatches(JsonElement root) => root.TryGetProperty("tok", out var tok) && tok.ValueKind == JsonValueKind.Number && tok.GetInt32() == _token;

	private void FlushPending()
	{
		var restarted = _everReady;
		_ready = true;
		_everReady = true;
		ApplyAccent();
		PostPolicy();
		var queued = _pending.ToList();
		_pending.Clear();
		foreach (var json in queued)
			_control!.CoreWebView2.PostWebMessageAsJson(json);

		// A "ready" after the first one means the page was reloaded or recovered and is empty: ask the owner to replay.
		if (restarted && queued.Count == 0)
			PageRestarted?.Invoke(this, EventArgs.Empty);
	}
}
