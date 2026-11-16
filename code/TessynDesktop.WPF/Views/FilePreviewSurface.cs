using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <see cref="IPreviewSurface"/> over its own WebView2 (created when the pane is first opened). WebView2 with a CSP was chosen over
/// native controls because markdown has to be rendered as HTML anyway, the same sandbox as the transcript then covers every kind,
/// and image and video playback come with it. The page (<c>preview.html</c>) is served from the virtual host <c>app.local</c> under
/// a CSP stricter than the transcript's: no script except its own file, and images and media only from <c>preview.local</c>, a
/// virtual host mapped to the preview cache folder. The surface exposes no host objects, cancels every navigation but the page's own,
/// refuses downloads, permissions, new windows and any request to another host, and passes links on to the owner.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FilePreviewSurface : IPreviewSurface
{
	private static readonly string PageUri = $"https://{Constants.Hosts.AppHost}/{Constants.Paths.PreviewPageFileName}";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private readonly WebViewEnvironmentProvider _environment;
	private readonly AppPaths _paths;
	private readonly UrlPolicy _policy;
	private readonly WebRequestPolicy _requests;
	private readonly IAppearanceService _appearance;
	private readonly ILogger<FilePreviewSurface> _logger;

	private WebView2? _control;
	private CoreWebView2Environment? _env;
	private bool _ready;
	private string? _pending;

	public FilePreviewSurface(WebViewEnvironmentProvider environment, AppPaths paths, UrlPolicy policy, WebRequestPolicy requests, IAppearanceService appearance, ILogger<FilePreviewSurface> logger)
	{
		_requests = requests;
		_appearance = appearance;
		_appearance.Changed += (_, _) => (_control?.Dispatcher ?? System.Windows.Application.Current?.Dispatcher)?.BeginInvoke(new Action(ApplyTheme));
		_environment = environment;
		_paths = paths;
		_policy = policy;
		_logger = logger;
	}

	public event EventHandler<string>? LinkClicked;

	public event EventHandler<PreviewRenderedEventArgs>? Rendered;

	/// <summary>Binds the surface to the pane's WebView2 control and loads the page.</summary>
	public async Task AttachAsync(WebView2 control)
	{
		_control = control;
		_ready = false;
		_env = await _environment.GetAsync();
		var controllerOptions = _env.CreateCoreWebView2ControllerOptions();
		controllerOptions.IsInPrivateModeEnabled = true;   // V11: no cookies or cache survive the run
		await control.EnsureCoreWebView2Async(_env, controllerOptions);
		var core = control.CoreWebView2;
		_logger.LogInformation("WebView2 private mode (preview): {Private}", core.Profile.IsInPrivateModeEnabled);

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

		Directory.CreateDirectory(_paths.PreviewCacheDirectory);
		core.SetVirtualHostNameToFolderMapping(Constants.Hosts.AppHost, _paths.WebAssetsDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
		core.SetVirtualHostNameToFolderMapping(Constants.Hosts.PreviewHost, _paths.PreviewCacheDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
		core.NavigationStarting += (_, e) => e.Cancel = e.Uri != PageUri;
		core.FrameNavigationStarting += (_, e) => e.Cancel = true;
		core.NewWindowRequested += (_, e) => e.Handled = true;
		core.DownloadStarting += (_, e) => e.Cancel = true;
		core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
		core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
		core.WebResourceRequested += OnWebResourceRequested;
		core.WebMessageReceived += OnWebMessageReceived;
		SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
		SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
		core.Navigate(PageUri);
	}

	public void ShowText(string text) => Send(new { cmd = "text", text });

	public void ShowMarkdown(string html) => Send(new { cmd = "markdown", html });

	public void ShowImage(string url) => Send(new { cmd = "image", url });

	public void ShowVideo(string url) => Send(new { cmd = "video", url });

	public void Clear() => Send(new { cmd = "clear" });

	private void Send(object message)
	{
		var json = JsonSerializer.Serialize(message, Json);
		if (!_ready || _control?.CoreWebView2 == null)
		{
			_pending = json; // only the newest content matters
			return;
		}

		_control.CoreWebView2.PostWebMessageAsJson(json);
	}

	private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
	{
		if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) && _requests.ForPreview(uri) == RequestDecision.Allow)
			return;

		_logger.LogInformation("Blocked a preview page request to a foreign host");
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
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ev", out var ev) || ev.ValueKind != JsonValueKind.String)
				return;

			switch (ev.GetString())
			{
				case "ready":
					_ready = true;
					ApplyAccent();
					ApplyTheme();
					if (_pending != null)
					{
						var json = _pending;
						_pending = null;
						_control!.CoreWebView2.PostWebMessageAsJson(json);
					}

					break;
				case "link":
					if (root.GetProperty("href").GetString() is { } href && _policy.IsAllowed(href))
						LinkClicked?.Invoke(this, href);
					break;
				case "rendered":
					_logger.LogInformation("Preview page rendered {Kind}: {Ok}", root.GetProperty("kind").GetString(), root.GetProperty("ok").GetBoolean());
					Rendered?.Invoke(this, new PreviewRenderedEventArgs(root.GetProperty("kind").GetString() ?? string.Empty, root.GetProperty("ok").GetBoolean()));
					break;
			}
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
		{
			_logger.LogWarning(ex, "Ignored a malformed preview page message");
		}
	}

	private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SystemParameters.WindowGlassColor))
			ApplyAccent();
	}

	private void ApplyTheme()
	{
		if (_control?.CoreWebView2 == null || !_ready)
			return;

		_control.CoreWebView2.PostWebMessageAsJson(PageAppearance.From(_appearance.IsDark, _appearance.Current).ToMessage());
	}

	private void ApplyAccent()
	{
		if (_control?.CoreWebView2 == null || !_ready)
			return;

		var accent = SystemParameters.WindowGlassColor;
		_ = _control.CoreWebView2.ExecuteScriptAsync($"document.documentElement.style.setProperty('--accent','#{accent.R:X2}{accent.G:X2}{accent.B:X2}')");
	}
}
