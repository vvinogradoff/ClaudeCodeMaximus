using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using ReactiveUI;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Owns the model/effort selector state and list for a single session (FR.12.3, FR.12.4, FR.12.14).
/// Extracted from SessionViewModel (Stage 7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ModelSelectionController : ReactiveObject, IDisposable
{
	/// <summary>Display names for the effort selector.</summary>
	public static string[] AvailableEfforts { get; } = ["Default", "Max", "High", "Medium", "Low"];

	/// <summary>Effort values passed to --effort flag. Empty string means no flag.</summary>
	private static readonly string[] EffortValues = ["", "max", "high", "medium", "low"];

	private readonly IClaudeModelService _modelService;
	private readonly IAppSettingsService _appSettings;
	private readonly DirectoryNodeModel? _directoryModel;
	private List<ClaudeModelInfo> _modelInfos;
	private int _selectedModelIndex;
	private int _selectedEffortIndex;
	private bool _isUpdatingModels;
	private bool _isChromeEnabled;

	/// <summary>Display names for the model selector (populated dynamically from IClaudeModelService).</summary>
	public ObservableCollection<string> AvailableModels { get; } = [];

	/// <summary>Selected model index (0=Default, 1..N=model entries). Persisted per directory as model ID string (FR.12.4).</summary>
	public int SelectedModelIndex
	{
		get => _selectedModelIndex;
		set
		{
			if (_isUpdatingModels) return;
			this.RaiseAndSetIfChanged(ref _selectedModelIndex, value);
			var modelId = (value > 0 && value <= _modelInfos.Count)
				? _modelInfos[value - 1].Id
				: string.Empty;
			if (_directoryModel != null)
				_directoryModel.SelectedModelId = modelId;
			_appSettings.Settings.SelectedModelId = modelId;
			_appSettings.Save();
			OnSelectedModelChanged();
		}
	}

	/// <summary>Returns the true model ID for --model, or null if Default is selected.</summary>
	public string? SelectedModelId
	{
		get
		{
			if (_selectedModelIndex <= 0 || _selectedModelIndex > _modelInfos.Count)
				return null;
			return _modelInfos[_selectedModelIndex - 1].Id;
		}
	}

	/// <summary>
	/// Returns the Ollama base URL when the selected model is a local Ollama model, or null for Anthropic models.
	/// Used to set ANTHROPIC_BASE_URL env var on claude CLI spawns (FR.12.14).
	/// </summary>
	public string? SelectedLocalBaseUrl
	{
		get
		{
			if (_selectedModelIndex <= 0 || _selectedModelIndex > _modelInfos.Count)
				return null;
			return _modelInfos[_selectedModelIndex - 1].Provider == ModelProvider.Ollama
				? _appSettings.Settings.OllamaBaseUrl
				: null;
		}
	}

	/// <summary>
	/// True when the selected local model does not support tool/function calling.
	/// Causes --tools "" to be appended so the CLI doesn't send tool definitions to the model.
	/// </summary>
	public bool SelectedModelDisablesTools =>
		_selectedModelIndex > 0 && _selectedModelIndex <= _modelInfos.Count
		&& !_modelInfos[_selectedModelIndex - 1].SupportsTools;

	/// <summary>
	/// Full <see cref="ClaudeModelInfo"/> for the selected model, or null when Default is selected.
	/// Used by <see cref="LocalToolProfileResolver"/> (FR.12.21) to decide whether the Local Tool
	/// Profile applies to this turn.
	/// </summary>
	public ClaudeModelInfo? SelectedModelInfo =>
		_selectedModelIndex > 0 && _selectedModelIndex <= _modelInfos.Count
			? _modelInfos[_selectedModelIndex - 1]
			: null;

	/// <summary>The directory node this session lives under, or null. Used by <see cref="LocalToolProfileResolver"/> (FR.12.21).</summary>
	public DirectoryNodeModel? DirectoryModel => _directoryModel;

	/// <summary>
	/// True when the selected model is a local Ollama model — gates visibility of the Local Tool
	/// Profile selector (FR.12.22). Anthropic models and "Default" never show it.
	/// </summary>
	public bool IsLocalModelSelected => SelectedModelInfo?.Provider == ModelProvider.Ollama;

	/// <summary>
	/// True when the Local Tool Profile selector should be interactive. False when the selected
	/// local model doesn't support tool calling at all (FR.12.13) — such models get zero tools
	/// regardless of any profile, so the selector is shown but disabled rather than hidden.
	/// </summary>
	public bool IsLocalToolSelectorEnabled => SelectedModelInfo?.SupportsTools ?? true;

	/// <summary>Tooltip for the Local Tool Profile toggle, explaining why it's disabled when applicable.</summary>
	public string ToolProfileToggleTooltip => IsLocalToolSelectorEnabled
		? "Local Tool Profile (FR.12.21): curate the native tools and MCP servers exposed to this local model."
		: "This model does not support tool calling.";

	/// <summary>Compact summary shown on the Local Tool Profile toggle button (e.g. "Tools: 5").</summary>
	public string ToolProfileSummary => $"Tools: {ToolProfileItems.Count(i => i.IsEnabled)}";

	/// <summary>
	/// One row per selectable native tool (<see cref="Constants.LocalTools.Catalog"/>), the cmx
	/// orchestration MCP server, and every registered External MCP Server Registry entry (FR.12.21).
	/// Rebuilt whenever the selected model changes; toggling a row persists immediately.
	/// </summary>
	public ObservableCollection<ToolProfileItemViewModel> ToolProfileItems { get; } = [];

	/// <summary>Selected effort index. 0=Default. Persisted per directory.</summary>
	public int SelectedEffortIndex
	{
		get => _selectedEffortIndex;
		set
		{
			this.RaiseAndSetIfChanged(ref _selectedEffortIndex, value);
			if (_directoryModel != null)
				_directoryModel.SelectedEffortIndex = value;
			_appSettings.Save();
		}
	}

	/// <summary>Returns the effort value for --effort flag, or null if Default is selected.</summary>
	public string? SelectedEffort =>
		_selectedEffortIndex > 0 && _selectedEffortIndex < EffortValues.Length
			? EffortValues[_selectedEffortIndex]
			: null;

	/// <summary>
	/// Experimental per-session toggle: when true, the claude spawn receives <c>--chrome</c> so the CLI
	/// attempts to connect to the Claude-in-Chrome extension for browser control. In-memory only
	/// (not persisted per directory); resets to off on session switch / app restart.
	/// </summary>
	public bool IsChromeEnabled
	{
		get => _isChromeEnabled;
		set => this.RaiseAndSetIfChanged(ref _isChromeEnabled, value);
	}

	public ModelSelectionController(IClaudeModelService modelService, IAppSettingsService appSettings, DirectoryNodeModel? directoryModel)
	{
		_modelService   = modelService;
		_appSettings    = appSettings;
		_directoryModel = directoryModel;

		// Populate model list from cache (instant; may update later via ModelsUpdated event)
		_modelInfos = new List<ClaudeModelInfo>(modelService.GetCachedModels());
		RebuildModelList();
		var savedModelId = directoryModel?.SelectedModelId ?? appSettings.Settings.SelectedModelId;
		_selectedModelIndex = string.IsNullOrEmpty(savedModelId)
			? 0
			: _modelInfos.FindIndex(m => m.Id == savedModelId) is var idx && idx >= 0 ? idx + 1 : 0;

		_modelService.ModelsUpdated += OnModelsUpdated;
		_selectedEffortIndex = Math.Clamp(
			directoryModel?.SelectedEffortIndex ?? 0,
			0, EffortValues.Length - 1);

		RebuildToolProfileItems();
	}

	/// <summary>Rebuilds the AvailableModels collection from _modelInfos. Index 0 is "Default"; entries show true model IDs (FR.12.3).</summary>
	private void RebuildModelList()
	{
		AvailableModels.Clear();
		AvailableModels.Add("Default");
		foreach (var m in _modelInfos)
			AvailableModels.Add(m.Id);
	}

	/// <summary>Called when the model service refreshes its list from the CLI.</summary>
	private void OnModelsUpdated(object? sender, EventArgs e)
	{
		var newModels = _modelService.GetCachedModels();

		// Save ID of currently selected model so we can re-select it after the list rebuilds
		var currentId = _selectedModelIndex > 0 && _selectedModelIndex <= _modelInfos.Count
			? _modelInfos[_selectedModelIndex - 1].Id
			: null;

		_isUpdatingModels = true;
		_modelInfos = new List<ClaudeModelInfo>(newModels);
		RebuildModelList();

		// Restore selection by ID; fall back to Default if the ID is no longer present
		if (currentId != null)
		{
			var idx = _modelInfos.FindIndex(m => m.Id == currentId);
			_selectedModelIndex = idx >= 0 ? idx + 1 : 0;
		}
		else
		{
			_selectedModelIndex = Math.Clamp(_selectedModelIndex, 0, _modelInfos.Count);
		}

		_isUpdatingModels = false;
		this.RaisePropertyChanged(nameof(SelectedModelIndex));
		OnSelectedModelChanged();
	}

	/// <summary>
	/// Restores model/effort selection from a session file's last recorded values, bypassing
	/// directory-model/settings persistence (switching sessions shows each session's own
	/// history rather than the shared per-directory values). Pass null/empty for either value
	/// to reset that selector to Default.
	/// </summary>
	public void RestoreFromEntry(string? modelId, string? effort)
	{
		if (!string.IsNullOrEmpty(modelId) && modelId != "default")
		{
			var idx = _modelInfos.FindIndex(m => m.Id == modelId);
			_selectedModelIndex = idx >= 0 ? idx + 1 : 0;
		}
		else
		{
			_selectedModelIndex = 0;
		}

		if (!string.IsNullOrEmpty(effort) && effort != "default")
		{
			var effortIdx = Array.IndexOf(EffortValues, effort);
			_selectedEffortIndex = effortIdx >= 0 ? effortIdx : 0;
		}
		else
		{
			_selectedEffortIndex = 0;
		}

		this.RaisePropertyChanged(nameof(SelectedModelIndex));
		this.RaisePropertyChanged(nameof(SelectedEffortIndex));
		OnSelectedModelChanged();
	}

	/// <summary>
	/// Refreshes everything that depends on which model is selected: the Local Tool Profile
	/// selector's visibility/enablement and its checkbox list (FR.12.21, FR.12.22).
	/// </summary>
	private void OnSelectedModelChanged()
	{
		RebuildToolProfileItems();
		this.RaisePropertyChanged(nameof(IsLocalModelSelected));
		this.RaisePropertyChanged(nameof(IsLocalToolSelectorEnabled));
		this.RaisePropertyChanged(nameof(ToolProfileToggleTooltip));
	}

	/// <summary>
	/// Rebuilds <see cref="ToolProfileItems"/> from <see cref="DirectoryNodeModel.LocalToolProfile"/>
	/// (or the default lean profile when null — <see cref="LocalToolProfileResolver"/>) and the
	/// currently registered External MCP Server Registry entries.
	/// </summary>
	private void RebuildToolProfileItems()
	{
		ToolProfileItems.Clear();

		var profile        = _directoryModel?.LocalToolProfile;
		var enabledNative   = profile?.NativeTools ?? new List<string>(Constants.LocalTools.DefaultNativeTools);
		var includeCmx      = profile?.IncludeCmxMcp ?? false;
		var enabledExternal = profile?.IncludedExternalMcpServerIds ?? new List<string>();

		foreach (var toolName in Constants.LocalTools.Catalog)
			ToolProfileItems.Add(new ToolProfileItemViewModel(
				toolName, ToolProfileItemKind.Native, null, enabledNative.Contains(toolName), PersistToolProfile));

		ToolProfileItems.Add(new ToolProfileItemViewModel(
			"cmx orchestration", ToolProfileItemKind.Cmx, null, includeCmx, PersistToolProfile));

		foreach (var server in _appSettings.Settings.ExternalMcpServers)
			ToolProfileItems.Add(new ToolProfileItemViewModel(
				server.DisplayName, ToolProfileItemKind.ExternalMcp, server.Id, enabledExternal.Contains(server.Id), PersistToolProfile));

		this.RaisePropertyChanged(nameof(ToolProfileSummary));
	}

	/// <summary>Writes the current checkbox states back to <see cref="DirectoryNodeModel.LocalToolProfile"/> immediately — no separate "Save" step in this UI.</summary>
	private void PersistToolProfile()
	{
		this.RaisePropertyChanged(nameof(ToolProfileSummary));

		if (_directoryModel == null)
			return;

		_directoryModel.LocalToolProfile = new LocalToolProfileModel
		{
			NativeTools = ToolProfileItems
				.Where(i => i.Kind == ToolProfileItemKind.Native && i.IsEnabled)
				.Select(i => i.Name)
				.ToList(),
			IncludeCmxMcp = ToolProfileItems
				.Any(i => i.Kind == ToolProfileItemKind.Cmx && i.IsEnabled),
			IncludedExternalMcpServerIds = ToolProfileItems
				.Where(i => i.Kind == ToolProfileItemKind.ExternalMcp && i.IsEnabled)
				.Select(i => i.ExternalId!)
				.ToList(),
		};
		_appSettings.Save();
	}

	public void Dispose() => _modelService.ModelsUpdated -= OnModelsUpdated;
}
