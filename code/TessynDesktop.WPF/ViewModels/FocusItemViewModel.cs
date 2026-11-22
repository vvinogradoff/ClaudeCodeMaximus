using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// One open item of a session's Focus view: kind, id (A1, A2, ...), title, markdown body, answer options, recommendation, references
/// and the free-text reply box. The instance outlives refreshes of the list (by item id), so text typed into the reply box survives.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FocusItemViewModel : ObservableObject
{
	private readonly FocusViewModel _owner;

	[ObservableProperty]
	private string _title = string.Empty;

	[ObservableProperty]
	private string? _body;

	[ObservableProperty]
	private AttentionKind _kind;

	[ObservableProperty]
	private string? _recommendation;

	/// <summary>The free-text answer typed into the card; sent by Reply or by "Send answers".</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ReplyCommand))]
	private string _replyText = string.Empty;

	public FocusItemViewModel(AttentionItemDto item, FocusViewModel owner, string nodeId = "")
	{
		_owner = owner;
		NodeId = nodeId;
		Id = item.Id;
		CreatedUtc = item.CreatedUtc;
		Number = item.Number;
		Update(item);
	}

	/// <summary>The session the item belongs to (the key of its reply draft, FR.23.19).</summary>
	public string NodeId { get; }

	/// <summary>The item id as the session and the user know it ("A3").</summary>
	public string Id { get; }

	public int Number { get; }

	public DateTimeOffset CreatedUtc { get; }

	/// <summary>The answer buttons in order: the options, then the recommendation last (an option equal to it is listed once, as the recommendation; FR.23.26).</summary>
	public ObservableCollection<FocusOptionViewModel> Options { get; } = [];

	private readonly List<string> _optionTexts = [];
	private string? _optionsRecommendation;

	public ObservableCollection<FocusRefViewModel> Refs { get; } = [];

	/// <summary>A path written in this card's title or body was activated (Ctrl+click or Invoke, FR.23.30).</summary>
	public void OpenPath(string path) => _owner.ActivatePath(this, path);

	public bool HasBody => !string.IsNullOrWhiteSpace(Body);

	public bool HasOptions => Options.Count > 0;

	public bool HasRecommendation => !string.IsNullOrWhiteSpace(Recommendation);

	public bool HasRefs => Refs.Count > 0;

	/// <summary>The questions of a questionnaire card (FR.23.35), in order; empty on any other card.</summary>
	public ObservableCollection<FocusQuestionViewModel> Questions { get; } = [];

	private string _questionSignature = string.Empty;

	public bool HasQuestions => Questions.Count > 0;

	/// <summary>The label above the reply box: <c>Note (optional)</c> on a questionnaire card, none on others.</summary>
	public string? NoteLabel => HasQuestions ? Constants.Questionnaire.NoteLabel : null;

	/// <summary>Lower-case kind name, for styling.</summary>
	public string KindKey => Kind.ToString().ToLowerInvariant();

	public string KindText => Kind.ToString();

	/// <summary>Decided items are a call the session already made; the close button reads "Got it" for them, like Critical and Alert.</summary>
	public string CloseText => Kind is AttentionKind.Ask or AttentionKind.Todo ? "Dismiss" : "Got it";

	/// <summary>True for an item that is no longer open: shown compact in the Closed expander (FR.23.14).</summary>
	public bool IsClosed => _status != AttentionStatus.Open;

	/// <summary>The outcome line of a closed card: <c>Answered: ...</c>, <c>Resolved: ...</c> or <c>Dismissed</c>.</summary>
	public string OutcomeText => _status switch
	{
		AttentionStatus.Answered => FormatOutcome(Constants.Texts.OutcomeAnswered, _answer),
		AttentionStatus.Resolved => FormatOutcome(Constants.Texts.OutcomeResolved, _resolutionNote),
		AttentionStatus.Dismissed => Constants.Texts.OutcomeDismissed,
		_ => string.Empty,
	};

	/// <summary>A closed card's body is hidden until the header row is clicked; an open card always shows it.</summary>
	[ObservableProperty]
	private bool _isBodyExpanded;

	/// <summary>Whether the body shows: always on open cards, after a header click on closed ones.</summary>
	public bool IsBodyVisible => HasBody && (!IsClosed || IsBodyExpanded);

	/// <summary>Posted time in local time: <c>HH:mm</c> today, otherwise <c>yyyy-MM-dd HH:mm</c> (FR.23.14).</summary>
	public string PostedText
	{
		get
		{
			var local = CreatedUtc.ToLocalTime();
			return local.ToString(local.Date == DateTime.Today ? "HH:mm" : "yyyy-MM-dd HH:mm");
		}
	}

	[RelayCommand]
	private void ToggleBody() => IsBodyExpanded = !IsBodyExpanded;

	partial void OnIsBodyExpandedChanged(bool value) => OnPropertyChanged(nameof(IsBodyVisible));

	private static string FormatOutcome(string label, string? detail) =>
		string.IsNullOrWhiteSpace(detail) ? label : string.Format(Constants.Texts.OutcomeFormat, label, detail);

	private AttentionStatus _status;
	private string? _answer;
	private string? _resolutionNote;

	/// <summary>Takes the fields the daemon may change (everything but the id and the reply box).</summary>
	public void Update(AttentionItemDto item)
	{
		_status = item.Status;
		_answer = item.Answer;
		_resolutionNote = item.ResolutionNote;
		OnPropertyChanged(nameof(IsClosed));
		OnPropertyChanged(nameof(OutcomeText));
		OnPropertyChanged(nameof(IsBodyVisible));
		Title = item.Title;
		Body = item.Body;
		Kind = item.Kind;
		Recommendation = item.Recommendation;

		var recommendation = string.IsNullOrWhiteSpace(item.Recommendation) ? null : item.Recommendation;
		if (!_optionTexts.SequenceEqual(item.Options) || _optionsRecommendation != recommendation)
		{
			_optionTexts.Clear();
			_optionTexts.AddRange(item.Options);
			_optionsRecommendation = recommendation;
			Options.Clear();
			var pick = new Action<string>(text => ReplyText = text);
			foreach (var option in item.Options)
				if (recommendation == null || !string.Equals(option.Trim(), recommendation.Trim(), StringComparison.OrdinalIgnoreCase))
					Options.Add(new FocusOptionViewModel(option, pick));
			if (recommendation != null)
				Options.Add(new FocusOptionViewModel(recommendation, pick, isRecommended: true));
			OnPropertyChanged(nameof(HasOptions));
		}

		SyncQuestions(item.Questions ?? []);

		var refs = item.Refs ?? [];
		if (!Refs.Select(r => r.Reference).SequenceEqual(refs))
		{
			Refs.Clear();
			foreach (var reference in refs)
				Refs.Add(new FocusRefViewModel(reference, _owner.ActivateRef, _owner.ActivateRefInSystemBrowser));
			OnPropertyChanged(nameof(HasRefs));
		}
	}

	partial void OnBodyChanged(string? value)
	{
		OnPropertyChanged(nameof(HasBody));
		OnPropertyChanged(nameof(IsBodyVisible));
	}

	partial void OnRecommendationChanged(string? value) => OnPropertyChanged(nameof(HasRecommendation));

	partial void OnKindChanged(AttentionKind value)
	{
		OnPropertyChanged(nameof(KindKey));
		OnPropertyChanged(nameof(KindText));
		OnPropertyChanged(nameof(CloseText));
	}

	/// <summary>True when the card has something to send: text in the reply box, or a questionnaire (its answers are the reply, FR.23.35).</summary>
	public bool HasReply => HasQuestions || !string.IsNullOrWhiteSpace(ReplyText);

	partial void OnReplyTextChanged(string value)
	{
		OnPropertyChanged(nameof(HasReply));
		_owner.OnReplyTextChanged(this);
	}

	[RelayCommand]
	private void AcceptRecommendation()
	{
		if (HasRecommendation)
			ReplyText = Constants.Texts.AcceptedRecommendation + Recommendation;
	}

	[RelayCommand(CanExecute = nameof(HasReply))]
	private Task ReplyAsync() => _owner.ReplyAsync(this);

	[RelayCommand]
	private Task DismissAsync() => _owner.DismissAsync(this);

	/// <summary>Builds the question view models again when the daemon sent other questions (a re-post replaces the whole questionnaire, FR.23.33); equal ones keep their answers.</summary>
	private void SyncQuestions(IReadOnlyList<AttentionQuestionDto> dtos)
	{
		var signature = QuestionSignature(dtos);
		if (signature == _questionSignature)
			return;

		_questionSignature = signature;
		Questions.Clear();
		foreach (var dto in dtos.OrderBy(q => q.Number))
			Questions.Add(new FocusQuestionViewModel(dto, _ => _owner.OnQuestionChanged(this)));
		OnPropertyChanged(nameof(HasQuestions));
		OnPropertyChanged(nameof(NoteLabel));
		OnPropertyChanged(nameof(HasReply));
		ReplyCommand.NotifyCanExecuteChanged();
	}

	private static string QuestionSignature(IReadOnlyList<AttentionQuestionDto> dtos) => string.Join('\u001e', dtos.Select(q =>
		string.Join('\u001f', q.Number.ToString(), q.Type.ToString(), q.Text, string.Join('\u001d', q.Choices), q.Recommended ?? string.Empty, string.Join('\u001d', q.RecommendedChoices))));

	/// <summary>The current value of every question, for <c>attention.reply</c> (FR.23.36); null on a card without a questionnaire.</summary>
	public IReadOnlyList<AttentionQuestionAnswerParams>? BuildAnswers() => HasQuestions ? Questions.Select(q => q.ToAnswer()).ToList() : null;

	/// <summary>The values to keep as a draft (FR.23.37): null while every question is at its start (nothing worth keeping), otherwise all of them.</summary>
	public IReadOnlyList<AttentionQuestionAnswerParams>? BuildDraftAnswers() => HasChangedQuestions ? BuildAnswers() : null;

	/// <summary>True when some question differs from its start (the recommendation, or empty).</summary>
	public bool HasChangedQuestions => Questions.Any(q => !q.IsAtStart);

	/// <summary>Shows kept values (a draft, FR.23.37) without saving them again. False, and nothing changes, when they do not fit this questionnaire (it was re-posted with other questions).</summary>
	public bool TryRestoreQuestions(IReadOnlyList<AttentionQuestionAnswerParams> answers)
	{
		if (!HasQuestions || answers.Count != Questions.Count || Questions.Where((q, i) => !q.Fits(answers[i])).Any())
			return false;

		for (var i = 0; i < Questions.Count; i++)
			Questions[i].Restore(answers[i]);
		return true;
	}
}
