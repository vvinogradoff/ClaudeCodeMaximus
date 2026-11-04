using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.ViewModels;

/// <summary>
/// The Focus view of one session (FR.23.12-FR.23.16): the Session/Focus selection (in memory only),
/// the Focus badge, and the cards of the session's Attention Ledger. Reaches the ledger only through
/// <see cref="IDaemonClient"/> and refreshes on the UI thread when <c>AttentionChanged</c> fires for its node.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusViewModel : ViewModelBase, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<FocusViewModel>();

	private readonly string _nodeId;
	private readonly IDaemonClient _daemonClient;
	private readonly IUiDispatcher _dispatcher;
	private readonly Func<string, Task> _sendPrompt;
	private readonly Dictionary<string, FocusItemViewModel> _cards = new(StringComparer.OrdinalIgnoreCase);

	private bool _isFocusSelected;
	private bool _isClosedExpanded;
	private bool _isDecisionsExpanded;
	private int _replyCount;
	private int _badgeCount;
	private AttentionKind? _badgeKind;

	/// <param name="nodeId">The session's NodeId; events of other nodes are ignored.</param>
	/// <param name="sendPrompt">Sends a prompt through the session's interactive send path without touching the input box or draft (FR.23.15).</param>
	public FocusViewModel(string nodeId, IDaemonClient daemonClient, IUiDispatcher dispatcher,
		Func<string, Task> sendPrompt)
	{
		_nodeId = nodeId;
		_daemonClient = daemonClient;
		_dispatcher = dispatcher;
		_sendPrompt = sendPrompt;

		var canExecute = this.WhenAnyValue(x => x.ReplyCount)
			.Select(count => count > 0);
		SendAnswersCommand = ReactiveCommand.Create(SendAnswers, canExecute);

		_daemonClient.AttentionChanged += OnAttentionChanged;
		Refresh();
	}

	public ObservableCollection<FocusItemViewModel> OpenItems { get; } = [];
	public ObservableCollection<FocusItemViewModel> DecidedItems { get; } = [];
	public ObservableCollection<FocusItemViewModel> ClosedItems { get; } = [];

	public ReactiveCommand<Unit, Unit> SendAnswersCommand { get; }

	/// <summary>True while the Focus view is selected. Not persisted; a new session starts on Session (FR.23.12).</summary>
	public bool IsFocusSelected
	{
		get => _isFocusSelected;
		set
		{
			// A segmented switch always has one side checked: unchecking the checked side is refused
			// and the bound toggle is re-pulled back to checked.
			if (!value)
			{
				this.RaisePropertyChanged();
				return;
			}

			SetSelection(true);
		}
	}

	public bool IsSessionSelected
	{
		get => !_isFocusSelected;
		set
		{
			if (!value)
			{
				this.RaisePropertyChanged();
				return;
			}

			SetSelection(false);
		}
	}

	/// <summary>Open items except Decided (FR.23.13).</summary>
	public int BadgeCount
	{
		get => _badgeCount;
		private set
		{
			this.RaiseAndSetIfChanged(ref _badgeCount, value);
			this.RaisePropertyChanged(nameof(BadgeText));
			this.RaisePropertyChanged(nameof(IsBadgeVisible));
		}
	}

	public string BadgeText => _badgeCount.ToString();

	/// <summary>Most severe kind among the counted items; selects the badge colour (FR.23.13).</summary>
	public AttentionKind? BadgeKind
	{
		get => _badgeKind;
		private set => this.RaiseAndSetIfChanged(ref _badgeKind, value);
	}

	/// <summary>The badge shows only while Session is selected and the count is above zero (FR.23.13).</summary>
	public bool IsBadgeVisible => !_isFocusSelected && _badgeCount > 0;

	public int ReplyCount
	{
		get => _replyCount;
		private set
		{
			this.RaiseAndSetIfChanged(ref _replyCount, value);
			this.RaisePropertyChanged(nameof(SendAnswersText));
		}
	}

	public string SendAnswersText => string.Format(Constants.Focus.SendAnswersFormat, _replyCount);

	/// <summary>Nothing needs attention: no open item other than Decided (FR.23.14).</summary>
	public bool IsEmpty => OpenItems.Count == 0;

	public string EmptyStateText => Constants.Focus.EmptyStateText;

	public bool HasDecisions => DecidedItems.Count > 0;
	public string DecisionsHeader => string.Format(Constants.Focus.DecisionsHeaderFormat, DecidedItems.Count);
	public bool HasClosed => ClosedItems.Count > 0;
	public string ClosedHeader => string.Format(Constants.Focus.ClosedHeaderFormat, ClosedItems.Count);

	/// <summary>Closed expander state: per session, collapsed whenever Focus gets selected, untouched by refreshes (FR.23.14).</summary>
	public bool IsClosedExpanded
	{
		get => _isClosedExpanded;
		set => this.RaiseAndSetIfChanged(ref _isClosedExpanded, value);
	}

	/// <summary>Decisions expander state: per session, collapsed by default (FR.23.14).</summary>
	public bool IsDecisionsExpanded
	{
		get => _isDecisionsExpanded;
		set => this.RaiseAndSetIfChanged(ref _isDecisionsExpanded, value);
	}

	/// <summary>Switches to the Session view (used by Enter in the output search box, FR.23.12).</summary>
	public void SelectSession() => SetSelection(false);

	public void Dispose() => _daemonClient.AttentionChanged -= OnAttentionChanged;

	private void SetSelection(bool focus)
	{
		if (_isFocusSelected == focus)
			return;

		_isFocusSelected = focus;
		if (focus)
			IsClosedExpanded = false;
		this.RaisePropertyChanged(nameof(IsFocusSelected));
		this.RaisePropertyChanged(nameof(IsSessionSelected));
		this.RaisePropertyChanged(nameof(IsBadgeVisible));
	}

	private void OnAttentionChanged(object? sender, AttentionChangedEventArgs e)
	{
		if (!string.Equals(e.NodeId, _nodeId, StringComparison.Ordinal))
			return;

		_dispatcher.Post(Refresh);
	}

	/// <summary>Re-reads the ledger and the summary for this node. Must run on the UI thread.</summary>
	private void Refresh()
	{
		var items = _daemonClient.GetAttentionItems(_nodeId);

		var open = new List<FocusItemViewModel>();
		var decided = new List<FocusItemViewModel>();
		var closed = new List<FocusItemViewModel>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// GetAttentionItems is already in FR.23.2 order, so each partition keeps it.
		foreach (var item in items)
		{
			seen.Add(item.Id);
			var card = GetOrCreateCard(item);
			if (item.Status != AttentionStatus.Open)
			{
				// A closed card has no reply box, so unsent text on it is dropped.
				card.ReplyText = string.Empty;
				closed.Add(card);
			}
			else if (item.Kind == AttentionKind.Decided)
				decided.Add(card);
			else
				open.Add(card);
		}

		foreach (var staleId in _cards.Keys.Where(id => !seen.Contains(id)).ToList())
			_cards.Remove(staleId);

		Sync(OpenItems, open);
		Sync(DecidedItems, decided);
		Sync(ClosedItems, closed);

		this.RaisePropertyChanged(nameof(IsEmpty));
		this.RaisePropertyChanged(nameof(HasDecisions));
		this.RaisePropertyChanged(nameof(DecisionsHeader));
		this.RaisePropertyChanged(nameof(HasClosed));
		this.RaisePropertyChanged(nameof(ClosedHeader));

		var summary = _daemonClient.GetAttentionSummary(_nodeId);
		BadgeKind = summary.TopKind;
		BadgeCount = summary.OpenCount;

		UpdateReplyCount();
	}

	private FocusItemViewModel GetOrCreateCard(AttentionItemModel item)
	{
		if (_cards.TryGetValue(item.Id, out var existing))
		{
			existing.Update(item);
			return existing;
		}

		var card = new FocusItemViewModel(item, UpdateReplyCount, CloseItem, card => SendCards([card]));
		_cards[item.Id] = card;
		return card;
	}

	/// <summary>Brings <paramref name="target"/> to the same sequence as <paramref name="desired"/> with minimal moves, so unchanged cards are not rebuilt.</summary>
	private static void Sync(ObservableCollection<FocusItemViewModel> target, List<FocusItemViewModel> desired)
	{
		for (var i = 0; i < desired.Count; i++)
		{
			if (i < target.Count && ReferenceEquals(target[i], desired[i]))
				continue;

			var currentIndex = target.IndexOf(desired[i]);
			if (currentIndex >= 0)
				target.Move(currentIndex, i);
			else
				target.Insert(i, desired[i]);
		}

		while (target.Count > desired.Count)
			target.RemoveAt(target.Count - 1);
	}

	/// <summary>Cards that can carry an answer, in on-screen order: open items, then Decisions.</summary>
	private IEnumerable<FocusItemViewModel> ReplyCards() => OpenItems.Concat(DecidedItems);

	private void UpdateReplyCount() => ReplyCount = ReplyCards().Count(c => c.HasReply);

	private void CloseItem(FocusItemViewModel card) => _daemonClient.CloseAttentionByUser(_nodeId, card.Id);

	/// <summary>
	/// "Send answers" (FR.23.15): one prompt from every non-empty reply box, sent through the session's
	/// interactive path. The items are marked answered BEFORE the send: the send builds the hidden
	/// instruction block (with the Pinned list) synchronously, and the turn that carries the answers must
	/// no longer list those items as pinned. If the send then fails, the items stay answered (logged).
	/// </summary>
	private void SendAnswers() => SendCards(ReplyCards().Where(c => c.HasReply).ToList());

	/// <summary>Sends the replies of the given cards as one prompt (the Reply button passes a single card, FR.23.15); other cards keep their text.</summary>
	private void SendCards(List<FocusItemViewModel> cards)
	{
		cards = cards.Where(c => c.HasReply).ToList();
		var answers = cards
			.Select(c => new AttentionAnswerModel { ItemId = c.Id, Title = c.Title, Answer = c.ReplyText.Trim() })
			.ToList();

		var prompt = AttentionPromptBuilder.BuildAnswersPrompt(answers);
		if (string.IsNullOrEmpty(prompt))
			return;

		_daemonClient.MarkAttentionAnswered(_nodeId, answers);
		foreach (var card in cards)
			card.ReplyText = string.Empty;

		Task sendTask;
		try
		{
			sendTask = _sendPrompt(prompt);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Focus view: sending answers for node {NodeId} failed; the items stay answered", _nodeId);
			return;
		}

		if (sendTask.IsFaulted)
		{
			_log.Warning(sendTask.Exception, "Focus view: sending answers for node {NodeId} failed; the items stay answered", _nodeId);
			return;
		}

		_ = LogLateFailureAsync(sendTask);
	}

	private async Task LogLateFailureAsync(Task sendTask)
	{
		try
		{
			await sendTask;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Focus view: turn started by Send answers for node {NodeId} failed", _nodeId);
		}
	}
}
