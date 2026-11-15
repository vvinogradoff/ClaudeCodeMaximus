using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One line of the local-tools checklist.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class LocalToolItemViewModel : ObservableObject
{
	public LocalToolItemViewModel(string id, string label, bool isChecked)
	{
		Id = id;
		Label = label;
		_isChecked = isChecked;
	}

	public string Id { get; }

	public string Label { get; }

	[ObservableProperty]
	private bool _isChecked;
}

/// <summary>
/// The Local Tool Profile of the open session's directory (FR.12.21 to FR.12.23): a Tools button that is a master switch on a short click and opens the
/// checklist when held, every change stored at once through <c>localTools.set</c>. Shown only while the selected model runs locally; dimmed when off.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class LocalToolsViewModel : ObservableObject
{
	private readonly ITessynClient _client;
	private readonly ILogger _logger;
	private readonly SessionOptionsViewModel _options;
	private string? _directoryId;
	private bool _isLocalModel;
	private LocalToolsDto? _profile;
	private bool _applying;
	private int _token;
	private int _storeSeq;

	[ObservableProperty]
	private bool _isVisible;

	[ObservableProperty]
	private bool _isEnabled;

	[ObservableProperty]
	private bool _isPopupOpen;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ToggleEnabledCommand))]
	private bool _isLoaded;

	[ObservableProperty]
	private bool _includeCmxMcp;

	public LocalToolsViewModel(ITessynClient client, SessionOptionsViewModel options, ILogger logger)
	{
		_client = client;
		_options = options;
		_logger = logger;
	}

	public ObservableCollection<LocalToolItemViewModel> NativeTools { get; } = [];

	public ObservableCollection<LocalToolItemViewModel> McpServers { get; } = [];

	public string Tooltip => IsEnabled ? Constants.ToolbarTexts.LocalToolsOnTip : Constants.ToolbarTexts.LocalToolsOffTip;

	/// <summary>Completes when the last read or change has been answered (tests).</summary>
	public Task Idle { get; private set; } = Task.CompletedTask;

	partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(Tooltip));

	/// <summary>A session opened (or none): the popup closes and nothing of the previous directory stays.</summary>
	public void Open(string? nodeId)
	{
		_token++;
		_directoryId = null;
		_profile = null;
		IsLoaded = false;
		IsPopupOpen = false;
		Fill(NativeTools, []);
		Fill(McpServers, []);
		UpdateVisibility();
	}

	/// <summary>The detail of the open session names its directory; the profile is read once per directory.</summary>
	public void ApplyDetail(string directoryId)
	{
		if (directoryId == _directoryId)
			return;

		_directoryId = directoryId;
		UpdateVisibility();
		Idle = LoadAsync(directoryId, ++_token);
	}

	/// <summary>The model the user picked; only a local one shows the control.</summary>
	public void SetModel(ModelProvider? provider)
	{
		_isLocalModel = provider == ModelProvider.Ollama;
		UpdateVisibility();
	}

	private void UpdateVisibility() => IsVisible = _isLocalModel && _directoryId != null;

	private async Task LoadAsync(string directoryId, int token)
	{
		try
		{
			var profile = await _client.LocalToolsGetAsync(directoryId);
			if (token == _token)
				Apply(profile);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "localTools.get failed for {DirectoryId}", directoryId);
		}
	}

	private void Apply(LocalToolsDto profile)
	{
		_applying = true;
		try
		{
			_profile = profile;
			IsEnabled = profile.Enabled;
			IncludeCmxMcp = profile.IncludeCmxMcp;
			Fill(NativeTools, profile.AvailableNativeTools.Select(t => new LocalToolItemViewModel(t, t, profile.NativeTools.Contains(t))));
			Fill(McpServers, profile.AvailableMcpServers.Select(s => new LocalToolItemViewModel(s.Id, s.DisplayName, profile.IncludedMcpServerIds.Contains(s.Id))));
			IsLoaded = true;
		}
		finally
		{
			_applying = false;
		}
	}

	private void Fill(ObservableCollection<LocalToolItemViewModel> target, IEnumerable<LocalToolItemViewModel> items)
	{
		foreach (var old in target)
			old.PropertyChanged -= OnItemChanged;

		target.Clear();
		foreach (var item in items)
		{
			item.PropertyChanged += OnItemChanged;
			target.Add(item);
		}
	}

	private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!_applying && e.PropertyName == nameof(LocalToolItemViewModel.IsChecked))
			Idle = StoreAsync(IsEnabled);
	}

	partial void OnIncludeCmxMcpChanged(bool value)
	{
		if (!_applying && _profile != null)
			Idle = StoreAsync(IsEnabled);
	}

	/// <summary>A short click: the master switch (FR.12.23.2); stored at once.</summary>
	[RelayCommand(CanExecute = nameof(IsLoaded))]
	private Task ToggleEnabledAsync() => Idle = StoreAsync(!IsEnabled);

	/// <summary>A held click: the checklist; clicking the button again while it is open only closes it.</summary>
	[RelayCommand]
	private void TogglePopup() => IsPopupOpen = !IsPopupOpen;

	[RelayCommand]
	private void ClosePopup() => IsPopupOpen = false;

	private async Task StoreAsync(bool enabled)
	{
		var directoryId = _directoryId;
		if (directoryId == null || _profile == null)
			return;

		var token = _token;
		var seq = ++_storeSeq;
		var native = NativeTools.Where(t => t.IsChecked).Select(t => t.Id).ToList();
		var servers = McpServers.Where(s => s.IsChecked).Select(s => s.Id).ToList();
		IsEnabled = enabled;   // shows at once
		try
		{
			var stored = await _client.LocalToolsSetAsync(directoryId, enabled, native, IncludeCmxMcp, servers);
			if (token == _token && seq == _storeSeq)   // a newer store has been sent since: this answer is stale
				_profile = stored;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "localTools.set failed for {DirectoryId}", directoryId);
			if (token == _token && seq == _storeSeq && _profile != null)
			{
				_options.Notice = Constants.ToolbarTexts.LocalToolsFailed + ex.Message;
				Apply(_profile);   // back to what the daemon has
			}
		}
	}
}
