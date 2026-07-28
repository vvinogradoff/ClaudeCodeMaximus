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
	}

	public void Dispose() => _modelService.ModelsUpdated -= OnModelsUpdated;
}
