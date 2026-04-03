using System;
using Avalonia;
using Avalonia.Media;
using TessynDesktop.Models;
using TessynDesktop.Services;
using ReactiveUI;

namespace TessynDesktop.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SessionNodeViewModel : ViewModelBase
{
	private string _name;
	private bool _isRunning;
	private bool _isResumable;
	private bool _isExternallyActive;
	private bool _isVisible = true;
	private bool _isBeingMoved;
	private bool _hasDraftText;
	private string? _lastPromptTime;
	private DateTimeOffset? _lastPromptTimestamp;
	private IBrush? _recencyBrush;

	public SessionNodeModel Model { get; }

	public string Name
	{
		get => _name;
		set
		{
			this.RaiseAndSetIfChanged(ref _name, value);
			Model.Name = value;
		}
	}

	public string FileName => Model.FileName;

	/// <summary>Stable daemon-side session identifier. Null until mapped or first run.system event.</summary>
	public string? ExternalId => Model.ExternalId;

	/// <summary>Best available identity key: ExternalId if set, otherwise FileName.</summary>
	public string SessionKey => Model.SessionKey;

	/// <summary>True while a claude process is actively running for this session.</summary>
	public bool IsRunning
	{
		get => _isRunning;
		set => this.RaiseAndSetIfChanged(ref _isRunning, value);
	}

	/// <summary>True when Claude Code still has this session available for --resume.</summary>
	public bool IsResumable
	{
		get => _isResumable;
		set => this.RaiseAndSetIfChanged(ref _isResumable, value);
	}

	/// <summary>True when this session is being actively used in an external terminal (Claude is thinking/responding).</summary>
	public bool IsExternallyActive
	{
		get => _isExternallyActive;
		set => this.RaiseAndSetIfChanged(ref _isExternallyActive, value);
	}

	/// <summary>True when the session's input text area contains text (a draft).</summary>
	public bool HasDraftText
	{
		get => _hasDraftText;
		set => this.RaiseAndSetIfChanged(ref _hasDraftText, value);
	}

	/// <summary>Controls visibility during search filtering.</summary>
	public bool IsVisible
	{
		get => _isVisible;
		set => this.RaiseAndSetIfChanged(ref _isVisible, value);
	}

	/// <summary>True while this session is being moved (F6 / drag). Drives semi-transparent visual.</summary>
	public bool IsBeingMoved
	{
		get => _isBeingMoved;
		set
		{
			this.RaiseAndSetIfChanged(ref _isBeingMoved, value);
			this.RaisePropertyChanged(nameof(MoveOpacity));
			this.RaisePropertyChanged(nameof(MoveForeground));
		}
	}

	/// <summary>Opacity when being moved (semi-transparent) vs normal.</summary>
	public double MoveOpacity => _isBeingMoved ? 0.4 : 1.0;

	/// <summary>Text brush when being moved (grey) vs default.</summary>
	public IBrush MoveForeground => _isBeingMoved
		? Brushes.Gray
		: (Application.Current!.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
			? Brushes.White : Brushes.Black);

	/// <summary>Formatted date/time of the last user prompt in this session.</summary>
	public string? LastPromptTime
	{
		get => _lastPromptTime;
		set => this.RaiseAndSetIfChanged(ref _lastPromptTime, value);
	}

	/// <summary>Actual timestamp of the last user prompt, used for recency calculations.</summary>
	public DateTimeOffset? LastPromptTimestamp
	{
		get => _lastPromptTimestamp;
		set
		{
			this.RaiseAndSetIfChanged(ref _lastPromptTimestamp, value);
			RefreshRecencyBrush();
		}
	}

	/// <summary>Background brush for the session node based on last prompt recency.</summary>
	public IBrush? RecencyBrush
	{
		get => _recencyBrush;
		private set => this.RaiseAndSetIfChanged(ref _recencyBrush, value);
	}

	/// <summary>Recalculates the recency brush based on how long ago the last prompt was.</summary>
	public void RefreshRecencyBrush()
	{
		if (_lastPromptTimestamp is null)
		{
			RecencyBrush = null;
			return;
		}

		var elapsed = DateTimeOffset.UtcNow - _lastPromptTimestamp.Value;
		string? key = elapsed.TotalMinutes switch
		{
			<= 15 => ThemeApplicator.KeyRecency15Min,
			<= 30 => ThemeApplicator.KeyRecency30Min,
			<= 60 => ThemeApplicator.KeyRecency60Min,
			_     => null,
		};

		if (key is null)
		{
			RecencyBrush = null;
			return;
		}

		if (Application.Current!.Resources.TryGetResource(key, null, out var resource) && resource is IBrush brush)
			RecencyBrush = brush;
		else
			RecencyBrush = null;
	}

	public SessionNodeViewModel(SessionNodeModel model)
	{
		Model = model;
		_name = model.Name;
	}
}
