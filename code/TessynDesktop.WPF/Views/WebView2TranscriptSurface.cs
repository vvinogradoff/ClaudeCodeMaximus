using System.Text.Json;
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
	private readonly ILogger<WebView2TranscriptSurface> _logger;
	private readonly List<string> _pending = new();

	private WebView2? _control;
	private CoreWebView2Environment? _env;
	private bool _ready;
	private bool _everReady;
	private int _token;
	private Task _sendTail = Task.CompletedTask;
	private int _generation;

	public WebView2TranscriptSurface(WebViewEnvironmentProvider environment, AppPaths paths, IUrlLauncher launcher, UrlPolicy policy, ILogger<WebView2TranscriptSurface> logger)
	{
		_environment = environment;
		_paths = paths;
		_launcher = launcher;
		_policy = policy;
		_logger = logger;
	}

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
		core.Navigate(PageUri);
	}

	public void Load(string key, IReadOnlyList<TranscriptEntryView> entries, bool hasOlder, bool toBottom)
	{
		_token++;
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

	private static object ToPage(TranscriptEntryView view) =>
		new { i = view.Index, r = view.Role, ts = view.Timestamp, html = view.Html, trunc = view.Truncated, full = view.FullLength, h = view.EstimatedHeight };

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
		if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) && uri.Host == HostName)
			return;

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
			}
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
		{
			_logger.LogWarning(ex, "Ignored a malformed page message");
		}
	}

	private bool TokenMatches(JsonElement root) => root.TryGetProperty("tok", out var tok) && tok.ValueKind == JsonValueKind.Number && tok.GetInt32() == _token;

	private void FlushPending()
	{
		var restarted = _everReady;
		_ready = true;
		_everReady = true;
		var queued = _pending.ToList();
		_pending.Clear();
		foreach (var json in queued)
			_control!.CoreWebView2.PostWebMessageAsJson(json);

		// A "ready" after the first one means the page was reloaded or recovered and is empty: ask the owner to replay.
		if (restarted && queued.Count == 0)
			PageRestarted?.Invoke(this, EventArgs.Empty);
	}
}
