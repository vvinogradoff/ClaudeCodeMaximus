using System;
using System.Collections.Generic;
using System.Reactive;
using ReactiveUI;
using TessynDesktop.Models;

namespace TessynDesktop.ViewModels;

/// <summary>
/// One card of the Focus view (FR.23.14): a snapshot of an <see cref="AttentionItemModel"/> plus the
/// card's unsent reply text. The instance is kept across refreshes (matched by item id) so the reply
/// text survives them (FR.23.16); <see cref="Update"/> swaps in the fresh snapshot.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusItemViewModel : ViewModelBase
{
	private readonly Action _replyChanged;
	private readonly Action<FocusItemViewModel> _close;
	private AttentionItemModel _model;
	private string _replyText = string.Empty;

	public FocusItemViewModel(AttentionItemModel model, Action replyChanged, Action<FocusItemViewModel> close)
	{
		_model = model;
		_replyChanged = replyChanged;
		_close = close;

		UseRecommendationCommand = ReactiveCommand.Create(UseRecommendation);
		CloseCommand = ReactiveCommand.Create(() => _close(this));
	}

	public ReactiveCommand<Unit, Unit> UseRecommendationCommand { get; }
	public ReactiveCommand<Unit, Unit> CloseCommand { get; }

	public string Id => _model.Id;
	public AttentionKind Kind => _model.Kind;
	public string KindLabel => _model.Kind.ToString();
	public string Title => _model.Title;
	public string Body => _model.Body;
	public bool HasBody => !string.IsNullOrWhiteSpace(_model.Body);
	public IReadOnlyList<string> Options => _model.Options;
	public bool HasOptions => _model.Options.Count > 0;
	public bool HasRecommendation => !string.IsNullOrWhiteSpace(_model.Recommendation);
	public string RecommendationButtonText => string.Format(Constants.Focus.RecommendationButtonFormat, _model.Recommendation);
	public bool IsOpen => _model.Status == AttentionStatus.Open;
	public bool IsClosed => !IsOpen;

	/// <summary>Posted time in local time: <c>HH:mm</c> today, otherwise <c>yyyy-MM-dd HH:mm</c> (FR.23.14).</summary>
	public string PostedText
	{
		get
		{
			var local = _model.CreatedUtc.ToLocalTime();
			var format = local.Date == DateTime.Today
				? Constants.Focus.PostedTodayFormat
				: Constants.Focus.PostedOtherDayFormat;
			return local.ToString(format);
		}
	}

	/// <summary>Outcome line of a closed card: <c>Answered: ...</c>, <c>Resolved: ...</c> or <c>Dismissed</c> (FR.23.14).</summary>
	public string OutcomeText => _model.Status switch
	{
		AttentionStatus.Answered => FormatOutcome(Constants.Focus.OutcomeAnswered, _model.Answer),
		AttentionStatus.Resolved => FormatOutcome(Constants.Focus.OutcomeResolved, _model.ResolutionNote),
		AttentionStatus.Dismissed => Constants.Focus.OutcomeDismissed,
		_ => string.Empty,
	};

	/// <summary>Close-button label by kind: Dismiss (Ask), Done (Todo), Got it (everything else) (FR.23.14).</summary>
	public string CloseLabel => _model.Kind switch
	{
		AttentionKind.Ask => Constants.Focus.CloseLabelDismiss,
		AttentionKind.Todo => Constants.Focus.CloseLabelDone,
		_ => Constants.Focus.CloseLabelGotIt,
	};

	public string ReplyText
	{
		get => _replyText;
		set
		{
			var next = value ?? string.Empty;
			if (string.Equals(_replyText, next, StringComparison.Ordinal))
				return;

			this.RaiseAndSetIfChanged(ref _replyText, next);
			this.RaisePropertyChanged(nameof(HasReply));
			_replyChanged();
		}
	}

	/// <summary>True when the reply box holds non-whitespace text, i.e. the card contributes to "Send answers".</summary>
	public bool HasReply => !string.IsNullOrWhiteSpace(_replyText);

	/// <summary>Replaces the item snapshot and re-raises everything derived from it. The reply text is untouched.</summary>
	public void Update(AttentionItemModel model)
	{
		_model = model;
		this.RaisePropertyChanged(nameof(Id));
		this.RaisePropertyChanged(nameof(Kind));
		this.RaisePropertyChanged(nameof(KindLabel));
		this.RaisePropertyChanged(nameof(Title));
		this.RaisePropertyChanged(nameof(Body));
		this.RaisePropertyChanged(nameof(HasBody));
		this.RaisePropertyChanged(nameof(Options));
		this.RaisePropertyChanged(nameof(HasOptions));
		this.RaisePropertyChanged(nameof(HasRecommendation));
		this.RaisePropertyChanged(nameof(RecommendationButtonText));
		this.RaisePropertyChanged(nameof(IsOpen));
		this.RaisePropertyChanged(nameof(IsClosed));
		this.RaisePropertyChanged(nameof(PostedText));
		this.RaisePropertyChanged(nameof(OutcomeText));
		this.RaisePropertyChanged(nameof(CloseLabel));
	}

	/// <summary>An option button was clicked: its text replaces the reply box content (FR.23.14).</summary>
	public void UseOption(string option) => ReplyText = option;

	private void UseRecommendation() =>
		ReplyText = string.Format(Constants.Focus.AcceptedRecommendationFormat, _model.Recommendation);

	private static string FormatOutcome(string label, string? detail) =>
		string.IsNullOrWhiteSpace(detail) ? label : string.Format(Constants.Focus.OutcomeFormat, label, detail);
}
