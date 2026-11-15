using Microsoft.Extensions.Logging;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The instruction toolbar, the command-bar toggles and the local-tools button of the open session (W4). They are views of the pane's session: this
/// part only tells them which session is open and what its detail says; each owns its own commands and daemon calls.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	/// <summary>The stored options of the open session (Auto-Commit, Auto-Document, completion sound).</summary>
	public SessionOptionsViewModel SessionOptions { get; private set; } = null!;

	public InstructionToolbarViewModel Instruction { get; private set; } = null!;

	public CommandTogglesViewModel Toggles { get; private set; } = null!;

	public LocalToolsViewModel LocalTools { get; private set; } = null!;

	/// <summary>The modal questions of the toolbar (Terminate, Streaming off); without it they go ahead unasked.</summary>
	public IDialogService? Dialogs { get; set; }

	private void InitializeToolbar()
	{
		SessionOptions = new SessionOptionsViewModel(_client, _ui, _logger);
		Instruction = new InstructionToolbarViewModel(_client, SessionOptions, _logger);
		Toggles = new CommandTogglesViewModel(_client, SessionOptions, _logger);
		LocalTools = new LocalToolsViewModel(_client, SessionOptions, _logger);
		Instruction.Connect(() => Dialogs, () => (PicksOfOpenSession().ModelId, PicksOfOpenSession().EffortId, PicksOfOpenSession().ProfileId));
		Toggles.Connect(() => Dialogs);
		Instruction.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(InstructionToolbarViewModel.AutoCompactChecked))
				SendCommand.NotifyCanExecuteChanged();
		};
		PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(SelectedModel))
				LocalTools.SetModel(SelectedModel?.Provider);
		};
	}

	partial void OnNodeIdChanged(string? value)
	{
		SessionOptions.Open(value);
		Instruction.Open(value);
		Toggles.Open(value);
		LocalTools.Open(value);
		LocalTools.SetModel(SelectedModel?.Provider);
	}

	partial void OnIsRunningChanged(bool value)
	{
		Instruction.IsRunning = value;
		OnRunningChangedForInput();
	}

	private void ApplyToolbarDetail(SessionDetailDto detail)
	{
		Toggles.ApplyDetail(detail.Id, detail.IsStreamingMode, detail.State.StreamingAlive);
		LocalTools.ApplyDetail(detail.DirectoryId);
	}
}
