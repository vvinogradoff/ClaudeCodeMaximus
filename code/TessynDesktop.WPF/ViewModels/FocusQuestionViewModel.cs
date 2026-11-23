using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// One question of a questionnaire card (FR.23.33, FR.23.35). Holds the current answer, which starts at the recommendation (a question without one starts
/// empty), and the rules of its controls: Yes/No is a radio group; Single is a radio group whose selected button clears when clicked again; Multi toggles each
/// choice on its own; Text is a box. Knows whether the answer differs from the recommendation (the Reset link) and turns itself into the
/// <see cref="AttentionQuestionAnswerParams"/> that is sent and drafted.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FocusQuestionViewModel : ObservableObject
{
	private readonly Action<FocusQuestionViewModel> _changed;
	private bool _quiet;

	[ObservableProperty]
	private string _answerText = string.Empty;

	public FocusQuestionViewModel(AttentionQuestionDto dto, Action<FocusQuestionViewModel> changed)
	{
		_changed = changed;
		Dto = dto;
		Number = dto.Number;
		Type = dto.Type;
		Text = dto.Text;
		Recommended = string.IsNullOrWhiteSpace(dto.Recommended) ? null : dto.Recommended.Trim();
		RecommendedChoices = dto.Type == AttentionQuestionType.Multi ? dto.RecommendedChoices : [];

		var choices = new List<FocusChoiceViewModel>();
		switch (dto.Type)
		{
			case AttentionQuestionType.YesNo:
				choices.Add(new FocusChoiceViewModel(this, Constants.Questionnaire.YesValue, Constants.Questionnaire.YesLabel, null, IsRecommendedValue(Constants.Questionnaire.YesValue)));
				choices.Add(new FocusChoiceViewModel(this, Constants.Questionnaire.NoValue, Constants.Questionnaire.NoLabel, null, IsRecommendedValue(Constants.Questionnaire.NoValue)));
				break;
			case AttentionQuestionType.Single:
			case AttentionQuestionType.Multi:
				for (var i = 0; i < dto.Choices.Count && i < Constants.Questionnaire.ChoiceLetters.Length; i++)
					choices.Add(new FocusChoiceViewModel(this, dto.Choices[i], dto.Choices[i], Constants.Questionnaire.ChoiceLetters[i].ToString(), IsRecommendedValue(dto.Choices[i])));
				break;
		}

		Choices = choices;
		ApplyStart();
	}

	/// <summary>The question as the daemon sent it; a refresh that brings an equal one keeps this view model (and the answer).</summary>
	public AttentionQuestionDto Dto { get; }

	public int Number { get; }

	public AttentionQuestionType Type { get; }

	public string Text { get; }

	public string? Recommended { get; }

	public IReadOnlyList<string> RecommendedChoices { get; }

	public IReadOnlyList<FocusChoiceViewModel> Choices { get; }

	/// <summary><c>Q1</c>.</summary>
	public string NumberText => string.Format(Constants.Questionnaire.NumberFormat, Number);

	public bool IsText => Type == AttentionQuestionType.Text;

	public bool HasChoices => !IsText;

	/// <summary>Yes/No sits on one line; Single and Multi list their choices vertically, as wide as the card.</summary>
	public bool IsYesNo => Type == AttentionQuestionType.YesNo;

	public string TextAutomationName => string.Format(Constants.Questionnaire.TextAutomationFormat, Number);

	public string ResetAutomationName => string.Format(Constants.Questionnaire.ResetAutomationFormat, Number);

	public string RecommendedMarker => Constants.Questionnaire.RecommendedMarker;

	public string ResetText => Constants.Questionnaire.Reset;

	/// <summary>True when the question has a recommendation to restore (a Multi one needs at least one recommended choice).</summary>
	public bool HasRecommendation => Type == AttentionQuestionType.Multi ? RecommendedChoices.Count > 0 : Recommended != null;

	/// <summary>True while the current answer equals the recommendation (false without one).</summary>
	public bool IsAtRecommendation => HasRecommendation && Type switch
	{
		AttentionQuestionType.Text => string.Equals(AnswerText.Trim(), Recommended, StringComparison.Ordinal),
		AttentionQuestionType.Multi => SelectedValues().OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
			.SequenceEqual(RecommendedChoices.Select(c => c.Trim()).OrderBy(v => v, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase),
		_ => SelectedValues() is [var only] && string.Equals(only, Recommended, StringComparison.OrdinalIgnoreCase),
	};

	/// <summary>The Reset link shows for a question whose answer differs from its recommendation.</summary>
	public bool ShowReset => HasRecommendation && !IsAtRecommendation;

	/// <summary>The <c>recommended</c> marker under the text box, while its text equals the recommendation.</summary>
	public bool ShowTextMarker => IsText && IsAtRecommendation;

	/// <summary>True while the answer is what the card started with (the recommendation, or empty without one): nothing to keep in a draft.</summary>
	public bool IsAtStart => HasRecommendation ? IsAtRecommendation : IsEmpty;

	private bool IsEmpty => IsText ? string.IsNullOrWhiteSpace(AnswerText) : Choices.All(c => !c.IsSelected);

	private bool IsRecommendedValue(string value) => Type == AttentionQuestionType.Multi
		? RecommendedChoices.Any(r => string.Equals(r.Trim(), value, StringComparison.OrdinalIgnoreCase))
		: Recommended != null && string.Equals(Recommended, value, StringComparison.OrdinalIgnoreCase);

	private List<string> SelectedValues() => Choices.Where(c => c.IsSelected).Select(c => c.Value).ToList();

	/// <summary>A click on <paramref name="choice"/> (the toggle button now wants <paramref name="selected"/>); applies the radio or independent rule of the question.</summary>
	internal void Choose(FocusChoiceViewModel choice, bool selected)
	{
		switch (Type)
		{
			case AttentionQuestionType.YesNo:
				SetOnly(selected ? choice : Choices.FirstOrDefault(c => c.IsSelected));   // a radio group: clicking the selected button keeps it
				break;
			case AttentionQuestionType.Single:
				SetOnly(selected ? choice : null);                                          // clicking the selected one again clears the answer
				break;
			case AttentionQuestionType.Multi:
				choice.SetSelected(selected);
				break;
		}

		AfterChange(notify: true);
	}

	private void SetOnly(FocusChoiceViewModel? chosen)
	{
		foreach (var choice in Choices)
			choice.SetSelected(ReferenceEquals(choice, chosen));
	}

	/// <summary>Puts the answer back to the recommendation; a question without one becomes empty.</summary>
	[RelayCommand]
	public void Reset()
	{
		ApplyStart();
		AfterChange(notify: true);
	}

	private void ApplyStart()
	{
		_quiet = true;
		try
		{
			foreach (var choice in Choices)
				choice.SetSelected(choice.IsRecommended);
			AnswerText = IsText ? Recommended ?? string.Empty : string.Empty;
		}
		finally
		{
			_quiet = false;
		}
	}

	partial void OnAnswerTextChanged(string value)
	{
		if (!_quiet)
			AfterChange(notify: true);
	}

	private void AfterChange(bool notify)
	{
		foreach (var choice in Choices)
			choice.Refresh();
		OnPropertyChanged(nameof(IsAtRecommendation));
		OnPropertyChanged(nameof(ShowReset));
		OnPropertyChanged(nameof(ShowTextMarker));
		OnPropertyChanged(nameof(IsAtStart));
		if (notify)
			_changed(this);
	}

	/// <summary>The value to send (<c>AttentionAnswerParams.Questions</c>, FR.23.36) and to keep as a draft (FR.23.37).</summary>
	public AttentionQuestionAnswerParams ToAnswer() => Type switch
	{
		AttentionQuestionType.Multi => new AttentionQuestionAnswerParams(Number, null, SelectedValues()),
		AttentionQuestionType.Text => new AttentionQuestionAnswerParams(Number, string.IsNullOrWhiteSpace(AnswerText) ? null : AnswerText.Trim()),
		_ => new AttentionQuestionAnswerParams(Number, SelectedValues().FirstOrDefault()),
	};

	/// <summary>True when <paramref name="answer"/> is a value this question accepts (its number, a known choice, the right shape).</summary>
	public bool Fits(AttentionQuestionAnswerParams answer)
	{
		if (answer.Number != Number)
			return false;

		var picked = answer.Choices ?? [];
		return Type switch
		{
			AttentionQuestionType.Text => answer.Choices == null,
			AttentionQuestionType.Multi => answer.Value == null && picked.All(HasChoice) && picked.Distinct(StringComparer.OrdinalIgnoreCase).Count() == picked.Count,
			_ => answer.Choices == null && (answer.Value == null || HasChoice(answer.Value)),
		};
	}

	private bool HasChoice(string value) => Choices.Any(c => string.Equals(c.Value, value.Trim(), StringComparison.OrdinalIgnoreCase));

	/// <summary>Shows a kept answer without raising the change (a draft restored from the daemon or the local cache). The caller checked <see cref="Fits"/>.</summary>
	public void Restore(AttentionQuestionAnswerParams answer)
	{
		_quiet = true;
		try
		{
			switch (Type)
			{
				case AttentionQuestionType.Text:
					AnswerText = answer.Value ?? string.Empty;
					break;
				case AttentionQuestionType.Multi:
					foreach (var choice in Choices)
						choice.SetSelected((answer.Choices ?? []).Any(c => string.Equals(c.Trim(), choice.Value, StringComparison.OrdinalIgnoreCase)));
					break;
				default:
					foreach (var choice in Choices)
						choice.SetSelected(answer.Value != null && string.Equals(answer.Value.Trim(), choice.Value, StringComparison.OrdinalIgnoreCase));
					break;
			}
		}
		finally
		{
			_quiet = false;
		}

		AfterChange(notify: false);
	}
}
