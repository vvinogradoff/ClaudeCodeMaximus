namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Autocomplete of the prompt input (W1b). The view reports what the user typed or where the caret went (<see cref="AutocompleteViewModel.Update"/>); a
/// change the pane makes itself (a draft arriving, a send clearing the box, a file drop) never opens the popup. Accepting a suggestion puts the text
/// into the input and the caret behind it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private int _programmaticInput;

	/// <summary>The popup of suggestions for the input.</summary>
	public AutocompleteViewModel Autocomplete { get; private set; } = null!;

	/// <summary>True while the pane itself is changing the input text: the view must not treat that as typing.</summary>
	public bool IsProgrammaticInput => _programmaticInput > 0;

	private void InitializeAutocomplete()
	{
		Autocomplete = new AutocompleteViewModel(_client, _ui, _logger, () => HasSession ? NodeId : null);
	}

	private void DisposeAutocomplete() => Autocomplete.Dispose();

	private void OnInputChangedForAutocomplete()
	{
		if (IsProgrammaticInput)
			Autocomplete.Close();
	}

	/// <summary>Sets the input text as the pane's own change: saved as the draft, but never taken for typing.</summary>
	private void SetInput(string text)
	{
		_programmaticInput++;
		try
		{
			InputText = text;
		}
		finally
		{
			_programmaticInput--;
		}
	}

	/// <summary>
	/// Accepts the selected suggestion (or the one at <paramref name="index"/>) into the input. Returns what was inserted, null when there was nothing to
	/// accept (the popup then closes). The caret is requested through <see cref="InputCaretRequested"/>.
	/// </summary>
	public AutocompleteInsertion? AcceptSuggestion(int index = -1)
	{
		var insertion = Autocomplete.Accept(InputText, index);
		if (insertion == null)
			return null;

		SetInput(insertion.Text);
		InputCaretRequested?.Invoke(this, insertion.Caret);
		return insertion;
	}
}
