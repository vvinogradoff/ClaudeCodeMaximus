using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// One preview window (FR.29.5–FR.29.8): the group it belongs to, the one-way Pin, the group's Auto-close switch, the title and the content, which is a file
/// renderer or a browser. Showing another kind disposes the old content; a file renderer is kept while the client stays the same. A web page's address is kept
/// current in <see cref="Item"/> so that a restart reopens the page that was shown. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class PreviewWindowViewModel : ObservableObject, IDisposable
{
	private readonly IPreviewSettingsService _settings;
	private readonly IPreviewContentFactory _content;
	private readonly IUiDispatcher _ui;
	private readonly PreviewAddressSanitizer _sanitizer = new();
	private readonly ITimer _settleTimer;
	private string? _routedAddress;
	private PreviewItemModel? _item;
	private ITessynClient? _fileClient;
	private PreviewBrowserContent? _browserContent;
	private bool _disposed;

	[ObservableProperty]
	private string _title = Constants.PreviewWindow.UntitledItem;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanAutoClose))]
	[NotifyPropertyChangedFor(nameof(PinToolTip))]
	[NotifyPropertyChangedFor(nameof(PinChecked))]
	[NotifyCanExecuteChangedFor(nameof(PinCommand))]
	private bool _isPinned;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(AutoCloseChecked))]
	private bool _autoClose;

	[ObservableProperty]
	private FilePreviewViewModel? _file;

	[ObservableProperty]
	private BrowserPreviewViewModel? _browser;

	public PreviewWindowViewModel(string groupId, string groupName, bool pinned, IPreviewSettingsService settings, IPreviewContentFactory content, IUiDispatcher ui, TimeProvider? time = null)
	{
		GroupId = groupId;
		GroupName = groupName;
		_isPinned = pinned;
		_settings = settings;
		_content = content;
		_ui = ui;
		_autoClose = ReadAutoClose();
		_title = groupName;
		_settleTimer = (time ?? TimeProvider.System).CreateTimer(_ => _ui.Post(SaveSettledAddress), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		_settings.GroupsChanged += OnGroupsChanged;
	}

	public string GroupId { get; }

	public string GroupName { get; private set; }

	/// <summary>The Auto-close button is shown on a group window only (FR.29.7).</summary>
	public bool CanAutoClose => !IsPinned;

	/// <summary>The Pin tooltip; once pinned it says so.</summary>
	public string PinToolTip => IsPinned ? Constants.PreviewWindow.PinnedToolTip : Constants.PreviewWindow.PinToolTip;

	/// <summary>
	/// The Pin button's checked state, settable so that the button also works through the Toggle pattern of UI Automation (a screen reader, a test), which flips a
	/// ToggleButton without running its command. Pinning is one-way: unchecking is refused.
	/// </summary>
	public bool PinChecked
	{
		get => IsPinned;
		set
		{
			if (value)
				Pin();
			else
				OnPropertyChanged();
		}
	}

	/// <summary>The Auto-close button's checked state, settable for the same reason as <see cref="PinChecked"/>; it changes the group's switch.</summary>
	public bool AutoCloseChecked
	{
		get => AutoClose;
		set
		{
			if (value != AutoClose)
				_settings.SetAutoClose(GroupId, value);
			else
				OnPropertyChanged();
		}
	}

	public string AutoCloseToolTip => Constants.PreviewWindow.AutoCloseToolTip;

	/// <summary>A copy of the item shown now; null before the first <see cref="Show"/>.</summary>
	public PreviewItemModel? Item => _item?.Clone();

	/// <summary>The browser content of a web page; the view needs its surface to attach the WebView2.</summary>
	public PreviewBrowserContent? BrowserContent => _browserContent;

	/// <summary>The user pinned the window.</summary>
	public event EventHandler? Pinned;

	/// <summary>The item changed inside the window: a web page settled on a new address on its own site, without sign-in parameters (FR.29.11).</summary>
	public event EventHandler? ItemChanged;

	/// <summary>Shows <paramref name="item"/>, replacing the content.</summary>
	public void Show(PreviewItemModel item, PreviewTarget? fileHandle, ITessynClient? client)
	{
		if (_disposed)
			return;

		_item = item.Clone();
		_routedAddress = item.Kind == PreviewItemKind.Url ? item.Url : null;
		_settleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		if (item.Kind == PreviewItemKind.Url)
			ShowPage(item.Url ?? string.Empty);
		else
			ShowFile(item, fileHandle, client);
	}

	/// <summary>The group was renamed: the title follows.</summary>
	public void SetGroupName(string name)
	{
		GroupName = name;
		RefreshTitle();
	}

	[RelayCommand(CanExecute = nameof(CanPin))]
	private void Pin()
	{
		if (IsPinned)
			return;

		IsPinned = true;
		Pinned?.Invoke(this, EventArgs.Empty);
	}

	private bool CanPin() => !IsPinned;

	[RelayCommand]
	private void ToggleAutoClose() => _settings.SetAutoClose(GroupId, !AutoClose);

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_settleTimer.Dispose();
		_settings.GroupsChanged -= OnGroupsChanged;
		DisposeFile();
		DisposeBrowser();
	}

	private void ShowFile(PreviewItemModel item, PreviewTarget? fileHandle, ITessynClient? client)
	{
		DisposeBrowser();
		if (client == null)
			return;

		if (File == null || !ReferenceEquals(_fileClient, client))
		{
			DisposeFile();
			_fileClient = client;
			File = _content.CreateFile(client);
		}

		RefreshTitle();
		if (fileHandle == null)
			File.ShowError(item.FileName ?? Constants.PreviewWindow.UntitledItem, Constants.PreviewWindow.NoFileHandleMessage);
		else
			_ = File.OpenAsync(fileHandle);
	}

	private void ShowPage(string url)
	{
		DisposeFile();
		if (_browserContent == null)
		{
			_browserContent = _content.CreateBrowser();
			var browser = _browserContent.ViewModel;
			browser.TitleChanged += OnPageTitleChanged;
			browser.PropertyChanged += OnBrowserPropertyChanged;
			Browser = browser;
		}

		_browserContent.ViewModel.Navigate(url);
		RefreshTitle();
	}

	private void OnPageTitleChanged(object? sender, EventArgs e) => _ui.Post(RefreshTitle);

	private void OnBrowserPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName is not (nameof(BrowserPreviewViewModel.Address) or nameof(BrowserPreviewViewModel.IsLoading)) || _item == null || _disposed)
			return;

		// The address is saved only once it has settled: every change (or the end of a load) restarts the wait.
		_settleTimer.Change(Constants.PreviewWindow.AddressSettleDelay, Timeout.InfiniteTimeSpan);
	}

	/// <summary>The page finished loading and kept its address for the settle delay: saves it when it is a new address on the routed site (FR.29.11).</summary>
	private void SaveSettledAddress()
	{
		if (_disposed || _item == null || Browser == null || Browser.IsLoading || Browser.HasError)
			return;

		var address = Browser.Address;
		if (string.IsNullOrEmpty(address) || !_sanitizer.IsSameOrigin(address, _routedAddress))
			return;

		var saved = _sanitizer.Sanitize(address);
		if (saved == _item.Url)
			return;

		_item.Url = saved;
		ItemChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>The title is <c>item - group</c>: the file name, or the page title (the host while the page has none).</summary>
	private void RefreshTitle()
	{
		if (_disposed)
			return;

		var name = _item?.Kind == PreviewItemKind.Url ? PageName() : _item?.FileName;
		Title = string.IsNullOrWhiteSpace(name) ? GroupName : string.Format(Constants.PreviewWindow.TitleFormat, name, GroupName);
	}

	private string? PageName()
	{
		var title = Browser?.Title;
		if (!string.IsNullOrWhiteSpace(title))
			return title;

		return Uri.TryCreate(_item?.Url, UriKind.Absolute, out var uri) ? uri.Host : _item?.Url;
	}

	private void DisposeFile()
	{
		var file = File;
		File = null;
		_fileClient = null;
		file?.Dispose();
	}

	private void DisposeBrowser()
	{
		var content = _browserContent;
		_browserContent = null;
		Browser = null;
		if (content == null)
			return;

		content.ViewModel.TitleChanged -= OnPageTitleChanged;
		content.ViewModel.PropertyChanged -= OnBrowserPropertyChanged;
		content.Dispose();
	}

	private bool ReadAutoClose() => _settings.Groups.FirstOrDefault(g => g.Id == GroupId)?.AutoClose ?? false;

	private void OnGroupsChanged(object? sender, EventArgs e) => _ui.Post(() =>
	{
		if (!_disposed)
			AutoClose = ReadAutoClose();
	});
}
