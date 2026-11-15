using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The MCP Servers tab (FR.20.1–20.9): the registry of external MCP servers with Add/Edit, Remove, enable, per-row Authorize and a status per server. Every change
/// goes to the daemon at once (FR.20.7), nothing waits for the window's Save. Contains no WPF types: the editor window is shown through <see cref="IMcpServerEditorPresenter"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class McpSectionViewModel : SettingsSectionViewModel
{
	private readonly ITessynClient _client;
	private readonly IDialogService _dialogs;
	private readonly IMcpServerEditorPresenter _editor;
	private readonly ILogger<McpSectionViewModel> _logger;
	private int _refreshGeneration;

	[ObservableProperty]
	private McpServerRowViewModel? _selected;

	public McpSectionViewModel(ITessynClient client, IDialogService dialogs, IMcpServerEditorPresenter editor, ILogger<McpSectionViewModel> logger)
	{
		_client = client;
		_dialogs = dialogs;
		_editor = editor;
		_logger = logger;
	}

	public override string Title => "MCP servers";

	public ObservableCollection<McpServerRowViewModel> Servers { get; } = [];

	public async Task InitializeAsync()
	{
		if (_client.ServerInfo?.Capabilities.Contains(RemoteProtocol.McpApi.Capability) != true)
		{
			MakeUnavailable(RemoteProtocol.McpApi.Capability);
			return;
		}

		await RefreshAsync();
	}

	/// <summary>Reads <c>mcp.list</c>; the selection and the per-row messages survive it.</summary>
	[RelayCommand]
	public async Task RefreshAsync()
	{
		// A burst of mcp.changed starts overlapping reads: only the newest one may apply its answer.
		var generation = Interlocked.Increment(ref _refreshGeneration);
		try
		{
			var result = await _client.McpListAsync();
			if (generation != Volatile.Read(ref _refreshGeneration))
				return;

			var selectedId = Selected?.Id;
			var previous = Servers.ToDictionary(r => r.Id);
			Servers.Clear();
			foreach (var server in result.Servers)
			{
				previous.TryGetValue(server.Id, out var old);
				Servers.Add(new McpServerRowViewModel(server) { Message = old?.Message, IsBusy = old?.IsBusy ?? false });
			}

			Selected = Servers.FirstOrDefault(r => r.Id == selectedId);
			ErrorText = null;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the MCP registry failed");
			ErrorText = ex.Message;
		}
	}

	[RelayCommand]
	public Task AddAsync() => EditCoreAsync(null);

	[RelayCommand]
	public Task EditAsync(McpServerRowViewModel? row) => row == null ? Task.CompletedTask : EditCoreAsync(row.Server);

	private async Task EditCoreAsync(McpServerDto? server)
	{
		using var editor = new McpServerEditorViewModel(_client, server, _logger);
		await _editor.ShowAsync(editor);
		if (editor.Saved)
			await RefreshAsync();
	}

	[RelayCommand]
	public async Task RemoveAsync(McpServerRowViewModel? row)
	{
		if (row == null)
			return;

		var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
			Constants.Settings.McpRemoveTitle, string.Format(Constants.Settings.McpConfirmRemove, row.DisplayName), "Remove", IsDestructive: true));
		if (!confirmed)
			return;

		try
		{
			await _client.McpRemoveAsync(row.Id);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Removing an MCP server failed");
			row.Message = ex.Message;
			return;
		}

		await RefreshAsync();
	}

	/// <summary>Switches a server on or off (<c>mcp.setEnabled</c>).</summary>
	[RelayCommand]
	public async Task ToggleEnabledAsync(McpServerRowViewModel? row)
	{
		if (row == null)
			return;

		try
		{
			await _client.McpSetEnabledAsync(row.Id, !row.Enabled);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Switching an MCP server failed");
			row.Message = ex.Message;
			return;
		}

		await RefreshAsync();
	}

	/// <summary>
	/// Runs the OAuth flow of one server: the browser opens on the daemon's machine and the call answers when the flow ends (up to a few minutes), so the row shows a busy state
	/// and a message meanwhile.
	/// </summary>
	[RelayCommand]
	public async Task AuthorizeAsync(McpServerRowViewModel? row)
	{
		if (row is not { CanAuthorize: true, IsBusy: false })
			return;

		row.IsBusy = true;
		row.Message = string.Format(Constants.Settings.McpAuthorizing, row.DisplayName);
		var id = row.Id;
		var name = row.DisplayName;
		string? message;
		try
		{
			var result = await _client.McpAuthorizeAsync(id);
			message = result.Authorized
				? string.Format(Constants.Settings.McpAuthorized, name)
				: string.Format(Constants.Settings.McpAuthorizeFailed, name, result.Error ?? "no reason given");
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Authorizing an MCP server failed");
			message = string.Format(Constants.Settings.McpAuthorizeFailed, name, ex.Message);
		}

		row.IsBusy = false;
		row.Message = message;
		await RefreshAsync();

		// A refresh while the browser was open made new row objects: the one for this server must stop being busy too.
		var current = Servers.FirstOrDefault(r => r.Id == id);
		if (current != null)
		{
			current.IsBusy = false;
			current.Message = message;
		}
	}

	/// <summary>An <c>mcp.changed</c> arrived (an edit elsewhere, or the end of an authorization).</summary>
	public Task OnChangedAsync() => IsAvailable ? RefreshAsync() : Task.CompletedTask;
}
