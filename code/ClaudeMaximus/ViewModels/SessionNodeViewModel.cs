using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SessionNodeViewModel : ViewModelBase
{
	private string _name;
	private bool _isRunning;
	private bool _isResumable;

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

	public SessionNodeViewModel(SessionNodeModel model)
	{
		Model = model;
		_name = model.Name;
	}
}
