using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The browser bar of a web preview window (FR.27.4): address, title, loading state, back/forward, reload or stop, and "Open in system
/// browser". It drives an <see cref="IBrowserSurface"/>; the surface raises its events on any thread, so every change is marshalled through
/// <see cref="IUiDispatcher"/>. Only http and https addresses are navigated. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class BrowserPreviewViewModel : ObservableObject, IDisposable
{
	private readonly IBrowserSurface _surface;
	private readonly UrlPolicy _policy;
	private readonly BrowserAddressInput _addressInput;
	private readonly IUrlLauncher _systemBrowser;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<BrowserPreviewViewModel> _logger;
	private bool _disposed;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(OpenInSystemBrowserCommand))]
	private string _address = string.Empty;

	/// <summary>What the address box shows and the user types (FR.27.10): the page's <see cref="Address"/> while the user is not editing.</summary>
	[ObservableProperty]
	private string _addressText = string.Empty;

	[ObservableProperty]
	private string _title = string.Empty;

	[ObservableProperty]
	private bool _isLoading;

	/// <summary>A failure (refused address, page not loaded, browser lost), shown under the bar until dismissed.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	private string? _error;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(BackCommand))]
	private bool _canGoBack;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ForwardCommand))]
	private bool _canGoForward;

	public BrowserPreviewViewModel(IBrowserSurface surface, UrlPolicy policy, IUrlLauncher systemBrowser, IUiDispatcher ui, ILogger<BrowserPreviewViewModel> logger, BrowserAddressInput? addressInput = null)
	{
		_addressInput = addressInput ?? new BrowserAddressInput(policy);
		_surface = surface;
		_policy = policy;
		_systemBrowser = systemBrowser;
		_ui = ui;
		_logger = logger;
		_surface.StateChanged += OnStateChanged;
		_surface.NavigationFailed += OnNavigationFailed;
		_surface.BrowserLost += OnBrowserLost;
	}

	/// <summary>Raised when <see cref="Title"/> changed (the window shows it as its title).</summary>
	public event EventHandler? TitleChanged;

	public bool HasError => !string.IsNullOrEmpty(Error);

	private bool _isEditingAddress;

	/// <summary>True while the address box has the keyboard focus: page navigations then leave <see cref="AddressText"/> alone; losing the focus shows the page's address again (FR.27.10).</summary>
	public bool IsEditingAddress
	{
		get => _isEditingAddress;
		set
		{
			if (!SetProperty(ref _isEditingAddress, value))
				return;

			if (!value)
				RevertAddress();
		}
	}

	partial void OnAddressChanged(string value)
	{
		if (!IsEditingAddress)
			AddressText = value;
	}

	/// <summary>Navigates to <paramref name="url"/>; anything but an absolute http or https URL sets <see cref="Error"/> and navigates nowhere.</summary>
	public void Navigate(string url)
	{
		if (!_policy.TryParse(url, out var uri))
		{
			Error = Constants.Browser.NotAllowedMessage;
			_logger.LogInformation("Refused to navigate the browser to a non-http(s) address");
			return;
		}

		Error = null;
		Address = uri.AbsoluteUri;
		UpdateTitle(null);
		_surface.Navigate(uri.AbsoluteUri);
	}

	[RelayCommand(CanExecute = nameof(CanGoBack))]
	private void Back() => _surface.GoBack();

	[RelayCommand(CanExecute = nameof(CanGoForward))]
	private void Forward() => _surface.GoForward();

	[RelayCommand]
	private void ReloadOrStop()
	{
		if (IsLoading)
			_surface.Stop();
		else
			_surface.Reload();
	}

	[RelayCommand(CanExecute = nameof(CanOpenInSystemBrowser))]
	private void OpenInSystemBrowser() => _systemBrowser.TryOpen(Address);

	/// <summary>Enter in the address box (FR.27.10): opens the typed address in this window; a bare host gets https, anything but http and https is refused with <see cref="Error"/>.</summary>
	[RelayCommand]
	private void GoToAddress()
	{
		if (string.IsNullOrWhiteSpace(AddressText))
		{
			RevertAddress();
			return;
		}

		if (!_addressInput.TryNormalize(AddressText, out var uri))
		{
			Error = Constants.Browser.TypedAddressRefusedMessage;
			_logger.LogInformation("Refused a typed address that is not http or https");
			return;
		}

		IsEditingAddress = false;
		Navigate(uri.AbsoluteUri);
	}

	/// <summary>Esc in the address box (FR.27.10): the address of the page shown comes back.</summary>
	[RelayCommand]
	private void RevertAddress()
	{
		AddressText = Address;
		Error = null;   // a refusal of the typed address goes with the text
	}

	[RelayCommand]
	private void DismissError() => Error = null;

	private bool CanOpenInSystemBrowser() => _policy.IsAllowed(Address);

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_surface.StateChanged -= OnStateChanged;
		_surface.NavigationFailed -= OnNavigationFailed;
		_surface.BrowserLost -= OnBrowserLost;
	}

	private void OnStateChanged(object? sender, BrowserStateChangedEventArgs e) => _ui.Post(() => ApplyState(e.IsLoading));

	private void OnNavigationFailed(object? sender, string status) => _ui.Post(() =>
	{
		if (!_disposed)
			Error = string.Format(Constants.Browser.NavigationFailedFormat, status);
	});

	private void OnBrowserLost(object? sender, EventArgs e) => _ui.Post(() =>
	{
		if (!_disposed)
			Error = Constants.Browser.BrowserLostMessage;
	});

	private void ApplyState(bool isLoading)
	{
		if (_disposed)
			return;

		IsLoading = isLoading;
		CanGoBack = _surface.CanGoBack;
		CanGoForward = _surface.CanGoForward;
		var source = _surface.Source;
		if (!string.IsNullOrEmpty(source) && source != Constants.Browser.BlankUri)
			Address = source;

		UpdateTitle(_surface.DocumentTitle);
	}

	/// <summary>The page title, or the address's host while the page has none.</summary>
	private void UpdateTitle(string? documentTitle)
	{
		var title = !string.IsNullOrWhiteSpace(documentTitle) ? documentTitle : HostOf(Address);
		if (title == Title)
			return;

		Title = title;
		TitleChanged?.Invoke(this, EventArgs.Empty);
	}

	private string HostOf(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
}
