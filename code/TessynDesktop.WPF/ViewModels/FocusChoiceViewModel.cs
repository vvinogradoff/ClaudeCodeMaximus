using CommunityToolkit.Mvvm.ComponentModel;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// One toggle button of a questionnaire question (FR.23.35): Yes or No, or a lettered choice. The button is checked while the choice is part of the answer; what a
/// click does to the other buttons of the question (radio behaviour, clearing, independent toggling) is decided by the question.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusChoiceViewModel : ObservableObject
{
	private readonly FocusQuestionViewModel _question;
	private bool _isSelected;

	public FocusChoiceViewModel(FocusQuestionViewModel question, string value, string label, string? letter, bool isRecommended)
	{
		_question = question;
		Value = value;
		Letter = letter;
		DisplayText = letter == null ? label : string.Format(Constants.Questionnaire.ChoiceFormat, letter, label);
		IsRecommended = isRecommended;
	}

	/// <summary>What is sent for this choice: <c>yes</c> / <c>no</c>, or the choice label exactly as stored.</summary>
	public string Value { get; }

	/// <summary>The letter A to D of a Single or Multi choice; null for Yes and No.</summary>
	public string? Letter { get; }

	/// <summary>What the button shows: <c>Yes</c>, or <c>A  label</c>.</summary>
	public string DisplayText { get; }

	/// <summary>True when the question's recommendation includes this choice: the button carries the <c>recommended</c> marker.</summary>
	public bool IsRecommended { get; }

	public string RecommendedMarker => Constants.Questionnaire.RecommendedMarker;

	public string AutomationName => string.Format(Constants.Questionnaire.ChoiceAutomationFormat, _question.Number, DisplayText);

	/// <summary>
	/// Two-way with the toggle button. The question applies its rule and tells every button of the question to read its state again; a click the rule
	/// refuses (the selected Yes/No button) is shown again after the binding finished writing, since WPF ignores a change raised inside the write.
	/// </summary>
	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			_question.Choose(this, value);

			if (_isSelected != value)
				SynchronizationContext.Current?.Post(_ => OnPropertyChanged(nameof(IsSelected)), null);
		}
	}

	/// <summary>Sets the state without any rule (the question does that); raises the change for the button.</summary>
	internal void SetSelected(bool value) => SetProperty(ref _isSelected, value, nameof(IsSelected));

	/// <summary>Raises the change even when the value stayed, so a button the user toggled shows the state the rule left.</summary>
	internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}
