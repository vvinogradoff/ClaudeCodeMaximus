using ReactiveUI;

namespace TessynDesktop.ViewModels;

/// <summary>
/// ViewModel for a single row in the key bindings settings list.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class KeyBindingEntryViewModel : ViewModelBase
{
	private string _binding;

	/// <summary>The action name (e.g., "ImportSessions", "CloseDialog").</summary>
	public string ActionName { get; }

	/// <summary>Human-friendly display label for the action.</summary>
	public string DisplayName => ActionName switch
	{
		Constants.KeyBindings.ImportSessions => "Import Sessions",
		Constants.KeyBindings.AddDirectory => "Add Directory",
		Constants.KeyBindings.OpenSettings => "Open Settings",
		Constants.KeyBindings.CloseDialog => "Close Dialog",
		Constants.KeyBindings.Send => "Send Message",
		_ => ActionName,
	};

	/// <summary>The key combo string (e.g., "Cmd+I", "Escape, Ctrl+W").</summary>
	public string Binding
	{
		get => _binding;
		set => this.RaiseAndSetIfChanged(ref _binding, value);
	}

	public KeyBindingEntryViewModel(string actionName, string binding)
	{
		ActionName = actionName;
		_binding = binding;
	}
}
