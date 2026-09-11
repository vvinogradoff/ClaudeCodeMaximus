using System;
using ReactiveUI;

namespace TessynDesktop.ViewModels;

/// <summary>Which part of a <see cref="TessynDesktop.Models.LocalToolProfileModel"/> a checkbox row edits.</summary>
/// <remarks>Created by Claude</remarks>
public enum ToolProfileItemKind
{
	/// <summary>A native tool name (e.g. "Read") — maps to <c>LocalToolProfileModel.NativeTools</c>.</summary>
	Native,

	/// <summary>The always-present cmx orchestration MCP server — maps to <c>LocalToolProfileModel.IncludeCmxMcp</c>.</summary>
	Cmx,

	/// <summary>An External MCP Server Registry entry — maps to <c>LocalToolProfileModel.IncludedExternalMcpServerIds</c>.</summary>
	ExternalMcp,
}

/// <summary>
/// One checkbox row in the Local Tool Profile popup (FR.12.21). Toggling <see cref="IsEnabled"/>
/// immediately persists the whole profile via the owning <c>ModelSelectionController</c>'s callback —
/// there is no separate "Save" step in this UI.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ToolProfileItemViewModel : ReactiveObject
{
	private bool _isEnabled;
	private readonly Action _onToggled;

	/// <summary>Display text for the checkbox: the native tool name, "cmx orchestration", or the external server's display name.</summary>
	public string Name { get; }

	public ToolProfileItemKind Kind { get; }

	/// <summary>The External MCP Server Registry entry ID (<see cref="TessynDesktop.Models.ExternalMcpServerModel.Id"/>). Null unless <see cref="Kind"/> is <see cref="ToolProfileItemKind.ExternalMcp"/>.</summary>
	public string? ExternalId { get; }

	public bool IsEnabled
	{
		get => _isEnabled;
		set
		{
			if (_isEnabled == value) return;
			this.RaiseAndSetIfChanged(ref _isEnabled, value);
			_onToggled();
		}
	}

	public ToolProfileItemViewModel(string name, ToolProfileItemKind kind, string? externalId, bool isEnabled, Action onToggled)
	{
		Name        = name;
		Kind        = kind;
		ExternalId  = externalId;
		_isEnabled  = isEnabled;
		_onToggled  = onToggled;
	}
}
