using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
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
	private string? _pendingLive;

	private readonly TranscriptMediaBroker _broker;
	private readonly WebRequestPolicy _requests;
	private readonly IAppearanceService _appearance;
	private readonly IClientSettingsStore _store;
	private readonly ScreensaverController _screensaver;
	private readonly UiCoalescer _appearanceCoalescer;

	private WebView2? _control;
	private CoreWebView2Environment? _env;
	private bool _ready;
	private bool _everReady;
	private int _token;
	private Task _sendTail = Task.CompletedTask;
	private int _generation;

	public WebView2TranscriptSurface(WebViewEnvironmentProvider environment, AppPaths paths, IUrlLauncher launcher, UrlPolicy policy, MediaPreviewPolicy media, TranscriptMediaBroker broker, WebRequestPolicy requests, IAppearanceService appearance, IClientSettingsStore store, ScreensaverController screensaver, ILogger<WebView2TranscriptSurface> logger)
	{
		_screensaver = screensaver;
		_appearanceCoalescer = new UiCoalescer(ApplyAppearance);
		_broker = broker;
		_appearance = appearance;
		_store = store;
		_requests = requests;
		_environment = environment;
		_paths = paths;
		_launcher = launcher;
		_policy = policy;
		_media = media;
		_logger = logger;
		_media.Changed += (_, _) => PostPolicy();
		_appearance.Changed += (_, _) => _appearanceCoalescer.Request(_control?.Dispatcher ?? System.Windows.Application.Current?.Dispatcher);
		_store.Changed += (_, e) =>
		{
			if (e.Section == Constants.SettingsSections.Colours)
				_appearanceCoalescer.Request(_control?.Dispatcher ?? System.Windows.Application.Current?.Dispatcher);
		};
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
		// An in-private profile (V11): nothing the page or a remote host could store (cookies, cache) survives the run. With the media proxy no
		// remote host reaches this WebView at all; the private mode is a second layer.
		var controllerOptions = _env.CreateCoreWebView2ControllerOptions();
		controllerOptions.IsInPrivateModeEnabled = true;
		await control.EnsureCoreWebView2Async(_env, controllerOptions);
		var core = control.CoreWebView2;
		_logger.LogInformation("WebView2 private mode (transcript): {Private}", core.Profile.IsInPrivateModeEnabled);

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
		settings.IsZoomControlEnabled = false;   // Ctrl+wheel changes the font sizes (W10a), not the page zoom

		Directory.CreateDirectory(_paths.PreviewCacheDirectory);
		core.SetVirtualHostNameToFolderMapping(HostName, _paths.WebAssetsDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
		core.SetVirtualHostNameToFolderMapping(Constants.Hosts.PreviewHost, _paths.PreviewCacheDirectory, CoreWebView2HostResourceAccessKind.DenyCors); // downloaded videos
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

	public event EventHandler? NewerRequested;

	public void NewerLoadFailed() => Post(() => JsonSerializer.Serialize(new { cmd = "newerFailed" }, Json));

	public void Load(string key, IReadOnlyList<TranscriptEntryView> entries, bool hasOlder, bool toBottom, bool hasNewer = false)
	{
		_token++;
		_broker.Reset();
		var token = _token;
		var snapshot = entries.ToList();
		Offer(snapshot);
		Post(() => JsonSerializer.Serialize(new { cmd = "load", tok = token, key, entries = snapshot.Select(ToPage), hasOlder, toBottom, hasNewer }, Json), replacesQueue: true);
	}

	public void Prepend(IReadOnlyList<TranscriptEntryView> entries, bool hasOlder)
	{
		var token = _token;
		var snapshot = entries.ToList();
		Offer(snapshot);
		Post(() => JsonSerializer.Serialize(new { cmd = "prepend", tok = token, entries = snapshot.Select(ToPage), hasOlder }, Json));
	}

	public void Append(IReadOnlyList<TranscriptEntryView> entries, bool hasNewer = false)
	{
		var token = _token;
		var snapshot = entries.ToList();
		Offer(snapshot);
		Post(() => JsonSerializer.Serialize(new { cmd = "append", tok = token, entries = snapshot.Select(ToPage), hasNewer }, Json));
	}

	public void Replace(TranscriptEntryView entry)
	{
		var token = _token;
		Offer([entry]);
		Post(() => JsonSerializer.Serialize(new { cmd = "replace", tok = token, entry = ToPage(entry) }, Json));
	}

	public void SetLive(TranscriptLiveView? live)
	{
		// Only the newest live state matters: a queued older one is replaced, never replayed.
		Post(() => JsonSerializer.Serialize(new { cmd = "live", live = live == null ? null : ToPage(live) }, Json), isLive: true);
	}

	private static object ToPage(TranscriptLiveView live) =>
		new
		{
			status = live.Status,
			text = live.Text,
			thinking = live.Thinking,
			tools = live.Tools.Select(t => new { id = t.Id, name = t.Name, summary = t.Summary, done = t.Done, isError = t.IsError }),
			queued = live.Queued,
			startedAt = live.StartedAtUnixMs,
			error = live.Error,
			usage = live.Usage,
		};

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

	/// <summary>Tells the broker which media URLs the page is about to receive, so it can later approve and serve exactly those.</summary>
	private void Offer(IEnumerable<TranscriptEntryView> entries)
	{
		foreach (var entry in entries)
			_broker.Offer(entry.Index, entry.Media);
	}

	private static object ToPage(TranscriptEntryView view) =>
		new { i = view.Index, r = view.Role, ts = view.Timestamp, html = view.Html, trunc = view.Truncated, full = view.FullLength, h = view.EstimatedHeight, m = view.Media ?? [], p = view.Paths ?? [], s = view.Source };

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
	private void Post(Func<string> build, bool replacesQueue = false, bool isLive = false)
	{
		var generation = _generation;
		var previous = _sendTail;
		var built = Task.Run(build);
		_sendTail = PostInOrderAsync(previous, built, generation, replacesQueue, isLive);
	}

	private async Task PostInOrderAsync(Task previous, Task<string> built, int generation, bool replacesQueue, bool isLive)
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
			{
				_pending.Clear();
				_pendingLive = null;
			}

			if (isLive)
				_pendingLive = json;
			else
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
		var decision = Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) ? _requests.ForTranscript(uri) : RequestDecision.Block;
		switch (decision)
		{
			case RequestDecision.Allow:
				return;
			case RequestDecision.ServeMedia:
				// The page asked the media proxy (a virtual host) for an image; the host decides and fetches (V2): nothing else leaves the app.
				_ = ServeMediaAsync(e, uri!, e.GetDeferral());
				return;
			default:
				_logger.LogInformation("Blocked a request to a foreign host");
				e.Response = _env!.CreateWebResourceResponse(null, 403, "Blocked", string.Empty);
				return;
		}
	}

	private async Task ServeMediaAsync(CoreWebView2WebResourceRequestedEventArgs e, Uri request, CoreWebView2Deferral deferral)
	{
		try
		{
			var image = await _broker.ServeImageAsync(request, CancellationToken.None);
			var stream = new MemoryStream(image.Bytes);
			e.Response = _env!.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {image.ContentType}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
			_logger.LogInformation("Media proxy served an image ({Bytes} bytes)", image.Bytes.Length);
		}
		catch (Exception ex)
		{
			_logger.LogInformation("Media proxy refused an image request: {Reason}", ex is MediaRefusedException ? ex.Message : ex.GetType().Name);
			e.Response = _env!.CreateWebResourceResponse(null, 403, "Refused", string.Empty);
		}
		finally
		{
			deferral.Complete();
		}
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
				case "needNewer" when TokenMatches(root):
					NewerRequested?.Invoke(this, EventArgs.Empty);
					break;
				case "expand" when TokenMatches(root):
					ExpandRequested?.Invoke(this, root.GetProperty("i").GetInt32());
					break;
				case "activity":
						_screensaver.NotePageActivity(DateTime.UtcNow);   // the page sees pointer, wheel and keys that WPF never does
						break;
					case "zoom":
					OnZoom(root);
					break;
				case "link":
					if (root.GetProperty("href").GetString() is { } href && _policy.IsAllowed(href))
						LinkClicked?.Invoke(this, href);
					break;
				case "liveShown":
					_logger.LogInformation("Live row shown by the page: {Status}, {Chars} chars, {Tools} tool(s), {Queued} queued",
						root.GetProperty("status").GetString(), root.GetProperty("chars").GetInt32(), root.GetProperty("tools").GetInt32(), root.GetProperty("queued").GetInt32());
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
					_ = ApproveMediaAsync(root.GetProperty("url").GetString(), root.GetProperty("i").GetInt32());
					break;
			}
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
		{
			_logger.LogWarning(ex, "Ignored a malformed page message");
		}
	}

	/// <summary>
	/// The user clicked "Load preview": the broker approves only a URL the host itself offered for that entry. An image is then fetched when the
	/// page requests it through the proxy; a video is downloaded to the preview cache first and the page plays it from there.
	/// </summary>
	private async Task ApproveMediaAsync(string? url, int entry)
	{
		var token = _token;
		if (url == null || !_broker.TryApprove(entry, url))
		{
			_logger.LogInformation("Ignored a media load request for a URL the host did not offer");
			return;
		}

		var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
		_logger.LogInformation("Media load approved by a click for host {Host}", host);
		string? src = string.Empty;
		var failed = false;
		if (uri != null && Constants.FileTypes.Video.Contains(Path.GetExtension(uri.AbsolutePath)))
		{
			try
			{
				src = await _broker.DownloadVideoAsync(url, CancellationToken.None);
			}
			catch (Exception ex)
			{
				_logger.LogInformation("Video download refused: {Reason}", ex is MediaRefusedException ? ex.Message : ex.GetType().Name);
				failed = true;
			}
		}

		if (token != _token)
			return;

		_control?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { cmd = failed ? "mediaFail" : "mediaOk", tok = token, i = entry, url, src }, Json));
	}

	private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SystemParameters.WindowGlassColor))
			ApplyAccent();
	}

	/// <summary>Hands the theme and the font sizes to the page (it sets the data-theme attribute and the --fs-* CSS variables).</summary>
	private void ApplyAppearance()
	{
		if (_control?.CoreWebView2 == null || !_ready)
			return;

		_control.CoreWebView2.PostWebMessageAsJson(PageAppearance.From(_appearance.IsDark, _appearance.Current, SystemRowColour()).ToMessage());
	}

	/// <summary>The palette colour of the system rows for the effective theme.</summary>
	private string SystemRowColour() =>
		ColourCatalog.Effective(ColourCatalog.Find(ColourCatalog.SystemRow)!, _store.Get<ColourSettings>().Overrides, _appearance.IsDark);

	/// <summary>Hands the Windows accent colour to the page as the --accent CSS variable.</summary>
	private void ApplyAccent()
	{
		if (_control?.CoreWebView2 == null || !_ready)
			return;

		var accent = SystemParameters.WindowGlassColor;
		_ = _control.CoreWebView2.ExecuteScriptAsync($"document.documentElement.style.setProperty('--accent','#{accent.R:X2}{accent.G:X2}{accent.B:X2}')");
	}

	/// <summary>Ctrl+wheel over an entry: the font size of what the entry shows changes by one point.</summary>
	private void OnZoom(JsonElement root)
	{
		var direction = root.TryGetProperty("dir", out var dir) && dir.ValueKind == JsonValueKind.Number ? Math.Sign(dir.GetInt32()) : 0;
		if (direction == 0)
			return;

		var target = (root.TryGetProperty("role", out var role) ? role.GetString() : null) switch
		{
			"U" => FontTarget.User,
			"A" => FontTarget.AssistantMarkdown,
			_ => FontTarget.Assistant,   // system and compaction rows and the live row show plain text
		};
		_appearance.AdjustFont(target, direction);
	}

	private bool TokenMatches(JsonElement root) => root.TryGetProperty("tok", out var tok) && tok.ValueKind == JsonValueKind.Number && tok.GetInt32() == _token;

	private void FlushPending()
	{
		var restarted = _everReady;
		_ready = true;
		_everReady = true;
		ApplyAccent();
		ApplyAppearance();
		PostPolicy();
		var queued = _pending.ToList();
		_pending.Clear();
		foreach (var json in queued)
			_control!.CoreWebView2.PostWebMessageAsJson(json);

		if (_pendingLive != null)
			_control!.CoreWebView2.PostWebMessageAsJson(_pendingLive);

		_pendingLive = null;

		// A "ready" after the first one means the page was reloaded or recovered and is empty: ask the owner to replay.
		if (restarted && queued.Count == 0)
			PageRestarted?.Invoke(this, EventArgs.Empty);
	}
}
