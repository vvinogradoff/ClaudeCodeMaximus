using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The file preview pane (FR.23.18, FR.26.5): reads the file behind a handle through <see cref="IFilePreviewLoader"/> (chunked, with
/// progress, cancel and size limits) and shows it on the sandboxed <see cref="IPreviewSurface"/>. Markdown is rendered (source/rendered
/// toggle), text and code are shown as monospace text (svg and html too: displayed, never rendered), images and video play from the
/// preview cache, and a PDF only offers "Open in default app". Opening externally is limited to <see cref="ExternalOpenPolicy"/>'s
/// allowlist and goes through <see cref="IExternalFileLauncher"/>. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FilePreviewViewModel : ObservableObject, IDisposable
{
	private readonly IFilePreviewLoader _loader;
	private readonly IPreviewSurface _surface;
	private readonly IMarkdownRenderer _markdown;
	private readonly IExternalFileLauncher _external;
	private readonly ExternalOpenPolicy _openPolicy;
	private readonly PreviewFileStore _store;
	private readonly IUrlLauncher _launcher;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<FilePreviewViewModel> _logger;

	private CancellationTokenSource? _cts;
	private int _loadToken;
	private string? _handle;
	private string? _rawName;
	private string? _cachedPath;
	private string? _markdownSource;
	private string? _markdownHtml;
	private Task _loadTask = Task.CompletedTask;

	[ObservableProperty]
	private bool _isOpen;

	[ObservableProperty]
	private string _title = string.Empty;

	[ObservableProperty]
	private bool _isLoading;

	[ObservableProperty]
	private double _progress;

	[ObservableProperty]
	private string? _message;

	/// <summary>A failure (read error, refused file), shown in the pane without blocking anything.</summary>
	[ObservableProperty]
	private string? _error;

	[ObservableProperty]
	private PreviewKind _kind;

	[ObservableProperty]
	private bool _showSource;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(OpenExternalCommand))]
	private bool _canOpenExternally;

	public FilePreviewViewModel(
		IFilePreviewLoader loader,
		IPreviewSurface surface,
		IMarkdownRenderer markdown,
		IExternalFileLauncher external,
		ExternalOpenPolicy openPolicy,
		PreviewFileStore store,
		IUrlLauncher launcher,
		IUiDispatcher ui,
		ILogger<FilePreviewViewModel> logger)
	{
		_loader = loader;
		_surface = surface;
		_markdown = markdown;
		_external = external;
		_openPolicy = openPolicy;
		_store = store;
		_launcher = launcher;
		_ui = ui;
		_logger = logger;
		_surface.LinkClicked += OnLinkClicked;
		_surface.Rendered += OnRendered;
	}

	/// <summary>True for a markdown file, which can be shown as rendered output or as source.</summary>
	public bool CanToggleSource => Kind == PreviewKind.Markdown && !IsLoading && Error == null;

	/// <summary>The size limit applies to a PDF too, but it is never shown; the pane only offers to open it.</summary>
	public bool IsPdf => Kind == PreviewKind.Pdf;

	/// <summary>Completes when the current load (if any) has finished.</summary>
	public Task WhenIdleAsync() => _loadTask;

	/// <summary>Opens the file behind a handle; replaces what the pane shows, cancelling a load still running.</summary>
	public Task OpenAsync(PreviewTarget target)
	{
		CancelCore();
		Reset();
		IsOpen = true;
		Title = target.Name;
		_handle = target.Handle;
		IsLoading = true;
		var token = ++_loadToken;
		var cts = _cts = new CancellationTokenSource();
		_loadTask = LoadAsync(target.Handle, token, cts.Token);
		return _loadTask;
	}

	/// <summary>Shows a failure instead of a file (for example <c>file.preview</c> refused the path).</summary>
	public void ShowError(string name, string message)
	{
		CancelCore();
		Reset();
		IsOpen = true;
		Title = name;
		Error = message;
	}

	private async Task LoadAsync(string handle, int token, CancellationToken cancellationToken)
	{
		try
		{
			var loaded = await _loader.LoadAsync(handle, saveToFile: false, new UiProgress(this, token), cancellationToken);
			if (token != _loadToken)
				return;

			// Markdown is parsed on a worker thread (up to 2 MB of pathological input must not freeze the pane), capped, and guarded: when it cannot
			// be rendered the source is shown instead (V6).
			string? html = null;
			var renderNote = (string?)null;
			if (loaded.Kind == PreviewKind.Markdown && loaded.Refusal == null)
			{
				var text = loaded.Text ?? string.Empty;
				var part = text.Length > Constants.Limits.MaxMarkdownChars ? text[..Constants.Limits.MaxMarkdownChars] : text;
				try
				{
					html = await Task.Run(() => _markdown.RenderMarkdown(part), cancellationToken);
					if (part.Length < text.Length)
						renderNote = "Rendered the first part only; the source shows all of it.";
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					_logger.LogInformation(ex, "Markdown could not be rendered; showing the source");
					renderNote = "The markdown could not be rendered; showing the source.";
				}

				if (token != _loadToken)
					return;
			}

			Apply(loaded, html, renderNote);
		}
		catch (OperationCanceledException)
		{
			if (token == _loadToken)
				Message = "Cancelled.";
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the preview file failed");
			if (token == _loadToken)
				Error = "Could not read the file: " + ex.Message;
		}
		finally
		{
			if (token == _loadToken)
				FinishLoading();
		}
	}

	private void Apply(LoadedPreview loaded, string? markdownHtml, string? renderNote)
	{
		_rawName = loaded.Name;
		Title = _openPolicy.DisplayName(loaded.Name);
		Kind = loaded.Kind;
		_logger.LogInformation("Preview opened: {Name} ({Kind}, {Size} bytes){Refused}", loaded.Name, loaded.Kind, loaded.Size, loaded.Refusal == null ? string.Empty : " refused");
		_cachedPath = loaded.FilePath;
		CanOpenExternally = _openPolicy.IsAllowed(loaded.Name) && loaded.Refusal == null;
		if (loaded.Refusal != null)
		{
			Error = loaded.Refusal;
			return;
		}

		switch (loaded.Kind)
		{
			case PreviewKind.Markdown:
				_markdownSource = loaded.Text ?? string.Empty;
				_markdownHtml = markdownHtml;
				if (markdownHtml == null)
					_surface.ShowText(_markdownSource);   // not renderable: the source
				else
					ShowCurrentMarkdown();
				Message = renderNote;
				break;
			case PreviewKind.Text:
				_surface.ShowText(loaded.Text ?? string.Empty);
				break;
			case PreviewKind.Image:
				_surface.ShowImage(_store.UrlOf(loaded.FilePath!));
				break;
			case PreviewKind.Video:
				_surface.ShowVideo(_store.UrlOf(loaded.FilePath!));
				break;
			case PreviewKind.Pdf:
				Message = Constants.Texts.PdfHint;
				break;
		}

		if (loaded.Truncated)
			Message = Constants.Texts.PreviewTruncated;
	}

	private void ShowCurrentMarkdown()
	{
		if (ShowSource)
			_surface.ShowText(_markdownSource ?? string.Empty);
		else
			_surface.ShowMarkdown(_markdownHtml ?? string.Empty);
	}

	partial void OnShowSourceChanged(bool value)
	{
		if (Kind == PreviewKind.Markdown && _markdownHtml != null)
			ShowCurrentMarkdown();
	}

	partial void OnKindChanged(PreviewKind value)
	{
		OnPropertyChanged(nameof(CanToggleSource));
		OnPropertyChanged(nameof(IsPdf));
	}

	partial void OnIsLoadingChanged(bool value)
	{
		OnPropertyChanged(nameof(CanToggleSource));
		CancelCommand.NotifyCanExecuteChanged();
		OpenExternalCommand.NotifyCanExecuteChanged();
	}

	partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(CanToggleSource));

	private void FinishLoading()
	{
		IsLoading = false;
		Progress = 0;
	}

	[RelayCommand(CanExecute = nameof(IsLoading))]
	private void Cancel() => _cts?.Cancel();

	[RelayCommand]
	private void Close()
	{
		CancelCore();
		Reset();
		IsOpen = false;
		Title = string.Empty;
		_surface.Clear();
	}

	private bool CanOpen() => CanOpenExternally && !IsLoading;

	/// <summary>Opens the file in the system's default app, for allowlisted document and media types only; downloads it first when it is not in the cache yet (a PDF, a markdown or text file).</summary>
	[RelayCommand(CanExecute = nameof(CanOpen))]
	private async Task OpenExternalAsync()
	{
		var name = _rawName ?? Title;   // the policy judges the real name, never the display form
		var handle = _handle;
		if (handle == null || !_openPolicy.IsAllowed(name))
			return;

		var token = _loadToken;
		try
		{
			var path = _cachedPath;
			if (path == null)
			{
				IsLoading = true;
				_cts = new CancellationTokenSource();
				var loaded = await _loader.LoadAsync(handle, saveToFile: true, new UiProgress(this, token), _cts.Token);
				if (token != _loadToken)
				{
					_store.Delete(loaded.FilePath);
					return;
				}

				if (loaded.Refusal != null)
				{
					Error = loaded.Refusal;
					return;
				}

				path = _cachedPath = loaded.FilePath;
			}

			if (path == null || !_external.TryOpen(path))
				Message = "The file could not be opened in the default app.";
		}
		catch (OperationCanceledException)
		{
			Message = "Cancelled.";
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Opening the preview file externally failed");
			Error = "Could not open the file: " + ex.Message;
		}
		finally
		{
			if (token == _loadToken)
				FinishLoading();
		}
	}

	private void OnLinkClicked(object? sender, string url) => _ui.Post(() => _launcher.TryOpen(url));

	private void OnRendered(object? sender, PreviewRenderedEventArgs e)
	{
		if (!e.Ok)
			_ui.Post(() => Error ??= "The preview could not be shown.");
	}

	private void CancelCore()
	{
		_loadToken++;
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = null;
		_store.Delete(_cachedPath);
		_cachedPath = null;
	}

	private void Reset()
	{
		_handle = null;
		_rawName = null;
		_markdownSource = null;
		_markdownHtml = null;
		Kind = PreviewKind.Unsupported;
		Error = null;
		Message = null;
		Progress = 0;
		IsLoading = false;
		ShowSource = false;
		CanOpenExternally = false;
		_surface.Clear();
	}

	public void Dispose()
	{
		CancelCore();
		_surface.LinkClicked -= OnLinkClicked;
		_surface.Rendered -= OnRendered;
	}

	private sealed class UiProgress : IProgress<PreviewProgress>
	{
		private readonly FilePreviewViewModel _owner;
		private readonly int _token;

		public UiProgress(FilePreviewViewModel owner, int token)
		{
			_owner = owner;
			_token = token;
		}

		public void Report(PreviewProgress value) => _owner._ui.Post(() =>
		{
			if (_token == _owner._loadToken)
				_owner.Progress = value.Fraction;
		});
	}
}
