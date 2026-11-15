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

	public FocusItemViewModel(AttentionItemDto item, FocusViewModel owner)
	{
		_owner = owner;
		Id = item.Id;
		CreatedUtc = item.CreatedUtc;
		Number = item.Number;
		Update(item);
	}

	/// <summary>The item id as the session and the user know it ("A3").</summary>
	public string Id { get; }

	public int Number { get; }

	public DateTimeOffset CreatedUtc { get; }

	public ObservableCollection<FocusOptionViewModel> Options { get; } = [];

	private readonly List<string> _optionTexts = [];

	public ObservableCollection<FocusRefViewModel> Refs { get; } = [];

	public bool HasBody => !string.IsNullOrWhiteSpace(Body);

	public bool HasOptions => Options.Count > 0;

	public bool HasRecommendation => !string.IsNullOrWhiteSpace(Recommendation);

	public bool HasRefs => Refs.Count > 0;

	/// <summary>Lower-case kind name, for styling.</summary>
	public string KindKey => Kind.ToString().ToLowerInvariant();

	public string KindText => Kind.ToString();

	/// <summary>Decided items are a call the session already made; the close button reads "Got it" for them, like Critical and Alert.</summary>
	public string CloseText => Kind is AttentionKind.Ask or AttentionKind.Todo ? "Dismiss" : "Got it";

	/// <summary>Takes the fields the daemon may change (everything but the id and the reply box).</summary>
	public void Update(AttentionItemDto item)
	{
		Title = item.Title;
		Body = item.Body;
		Kind = item.Kind;
		Recommendation = item.Recommendation;

		if (!_optionTexts.SequenceEqual(item.Options))
		{
			_optionTexts.Clear();
			_optionTexts.AddRange(item.Options);
			Options.Clear();
			foreach (var option in item.Options)
				Options.Add(new FocusOptionViewModel(option, text => ReplyText = text));
			OnPropertyChanged(nameof(HasOptions));
		}

		var refs = item.Refs ?? [];
		if (!Refs.Select(r => r.Reference).SequenceEqual(refs))
		{
			Refs.Clear();
			foreach (var reference in refs)
				Refs.Add(new FocusRefViewModel(reference, _owner.ActivateRef));
			OnPropertyChanged(nameof(HasRefs));
		}
	}

	partial void OnBodyChanged(string? value) => OnPropertyChanged(nameof(HasBody));

	partial void OnRecommendationChanged(string? value) => OnPropertyChanged(nameof(HasRecommendation));

	partial void OnKindChanged(AttentionKind value)
	{
		OnPropertyChanged(nameof(KindKey));
		OnPropertyChanged(nameof(KindText));
		OnPropertyChanged(nameof(CloseText));
	}

	/// <summary>True when the reply box holds something to send.</summary>
	public bool HasReply => !string.IsNullOrWhiteSpace(ReplyText);

	partial void OnReplyTextChanged(string value)
	{
		OnPropertyChanged(nameof(HasReply));
		_owner.OnReplyTextChanged();
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
}
