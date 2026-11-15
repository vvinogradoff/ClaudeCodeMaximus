using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// ISSUE.013 R1 in the client: switches a session to another profile. It asks which profile; when the session has workers (descendants) it asks whether to move
/// them too and lists them, running ones as queued (they are never interrupted); calls <c>profile.switch</c> and reports moved, queued and failed sessions.
/// Contains no WPF types; the questions go through <see cref="IDialogService"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ProfileSwitchViewModel : ObservableObject
{
	/// <summary>Safety bound of descendants read, so a runaway tree cannot make the dialog hang; the dialog says "at least N" when it is reached.</summary>
	public const int DefaultMaxDescendants = 5000;

	private readonly int _maxDescendants;

	private readonly IDialogService _dialogs;
	private readonly TreeContext _tree;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<ProfileSwitchViewModel> _logger;
	private ITessynClient? _client;

	/// <summary>The one-line result under the tree: an error, or "nothing changed"; empty otherwise.</summary>
	[ObservableProperty]
	private string _statusText = string.Empty;

	/// <summary>True when the daemon announces <c>profiles.admin</c> (not on a relay, not on an older daemon): without it the switch is not offered.</summary>
	[ObservableProperty]
	private bool _isAvailable;

	public ProfileSwitchViewModel(IDialogService dialogs, TreeContext tree, IUiDispatcher ui, ILogger<ProfileSwitchViewModel> logger, int maxDescendants = DefaultMaxDescendants)
	{
		_maxDescendants = maxDescendants;
		_dialogs = dialogs;
		_tree = tree;
		_ui = ui;
		_logger = logger;
	}

	public void Attach(ITessynClient client)
	{
		_client = client;
		IsAvailable = client.ServerInfo?.Capabilities.Contains(RemoteProtocol.ProfilesAdmin.Capability) == true;
	}

	/// <summary>The summary of the last switch that reached the daemon; null before one or after a cancelled one.</summary>
	public ProfileSwitchSummary? LastSummary { get; private set; }

	/// <summary>The error of the last switch, null when it succeeded or was cancelled.</summary>
	public string? LastError { get; private set; }

	/// <summary>True when the node is a session the command applies to.</summary>
	public static bool CanSwitch(TreeNodeViewModel? node) => node is { IsSessionLike: true };

	public async Task SwitchAsync(TreeNodeViewModel? node)
	{
		LastSummary = null;
		LastError = null;
		StatusText = string.Empty;
		var client = _client;
		if (client == null || !IsAvailable || !CanSwitch(node))
			return;

		try
		{
			var target = await PickProfileAsync(client, node!);
			if (target == null)
				return;

			var profileId = target.Id;
			var descendants = await ReadDescendantsAsync(client, node!.Id);
			var includeDescendants = false;
			if (descendants.Items.Count > 0)
			{
				var answer = await AskCascadeAsync(node, target, descendants);
				if (answer == null)
					return;

				includeDescendants = answer == Constants.Profiles.CascadeAllId;
			}

			var result = await client.ProfileSwitchAsync(node.Id, profileId, includeDescendants);
			_ui.Post(() => Apply(result, node));
			var summary = ProfileSwitchSummary.From(result, node.Id, node.Label);
			LastSummary = summary;
			_logger.LogInformation("Switched {NodeId} to profile {Profile}: moved {Moved}, queued {Queued}, failed {Failed}",
				node.Id, result.ProfileName, summary.MovedCount, result.Queued.Count, result.Failed.Count);
			if (!summary.NeedsAttention && summary.MovedCount == 0)
				StatusText = string.Format(Constants.Profiles.ResultNothingChanged, node.Label, result.ProfileName);

			if (summary.NeedsAttention)
				await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
					Constants.Profiles.ResultTitle, summary.ToText(node.Label), Constants.Profiles.ResultOk, Constants.DialogTexts.Cancel));
		}
		catch (Exception ex)
		{
			LastError = ex.Message;
			StatusText = ex.Message;
			_logger.LogWarning(ex, "Switching the profile of {NodeId} failed", node!.Id);
		}
	}

	private async Task<ProfileDto?> PickProfileAsync(ITessynClient client, TreeNodeViewModel node)
	{
		var profiles = (await client.ProfileListAsync()).Profiles;
		var current = node.ProfileName;
		var offered = profiles
			.Where(p => !string.Equals(p.DisplayName, current, StringComparison.OrdinalIgnoreCase))
			.ToList();
		var choices = offered
			.Select(p => new DialogChoice(p.Id, p.AuthState == ProfileAuthState.LoggedOut ? $"{p.DisplayName} ({Constants.Profiles.AuthStateLoggedOut})" : p.DisplayName))
			.ToList();
		if (choices.Count == 0)
			return null;

		var picked = await _dialogs.ChoiceAsync(new ChoiceDialogRequest(Constants.Profiles.PickTitle, string.Format(Constants.Profiles.PickMessage, node.Label), choices));
		return offered.FirstOrDefault(p => p.Id == picked);
	}

	/// <summary>The descendants found; <see cref="Truncated"/> when the safety bound stopped the read, so the real number is larger.</summary>
	private sealed record Descendants(IReadOnlyList<TreeSessionDto> Items, bool Truncated);

	/// <summary>All descendants of the session through <c>tree.workers</c>, breadth first; stops at the safety bound.</summary>
	private async Task<Descendants> ReadDescendantsAsync(ITessynClient client, string nodeId)
	{
		var found = new List<TreeSessionDto>();
		var seen = new HashSet<string> { nodeId };
		var queue = new Queue<string>();
		queue.Enqueue(nodeId);
		while (queue.Count > 0)
		{
			if (found.Count >= _maxDescendants)
				return new Descendants(found, Truncated: true);

			var workers = (await client.TreeWorkersAsync(queue.Dequeue())).Workers;
			foreach (var worker in workers)
			{
				if (!seen.Add(worker.Id))
					continue;

				found.Add(worker);
				if (worker.WorkerCount > 0)
					queue.Enqueue(worker.Id);
			}
		}

		return new Descendants(found, Truncated: false);
	}

	private async Task<string?> AskCascadeAsync(TreeNodeViewModel node, ProfileDto target, Descendants descendants)
	{
		var count = descendants.Truncated ? string.Format(Constants.Profiles.DescendantsAtLeast, descendants.Items.Count) : descendants.Items.Count.ToString();
		var message = new StringBuilder();
		message.AppendLine(string.Format(Constants.Profiles.CascadeMessage, node.Label, count, target.DisplayName));
		foreach (var worker in descendants.Items.Take(Constants.Profiles.MaxListedDescendants))
		{
			var on = worker.State.PinnedProfileName ?? worker.State.ProfileName ?? "?";
			message.AppendLine("  • " + string.Format(worker.State.Running ? Constants.Profiles.DescendantRunning : Constants.Profiles.DescendantIdle, worker.Name, on));
		}

		if (descendants.Items.Count > Constants.Profiles.MaxListedDescendants)
		{
			var more = descendants.Items.Count - Constants.Profiles.MaxListedDescendants;
			message.AppendLine("  " + string.Format(Constants.Profiles.DescendantMore, descendants.Truncated ? string.Format(Constants.Profiles.DescendantsAtLeast, more) : more.ToString()));
		}

		return await _dialogs.ChoiceAsync(new ChoiceDialogRequest(
			Constants.Profiles.CascadeTitle,
			message.ToString().TrimEnd(),
			[new DialogChoice(Constants.Profiles.CascadeAllId, Constants.Profiles.CascadeAllLabel), new DialogChoice(Constants.Profiles.CascadeOnlyId, Constants.Profiles.CascadeOnlyLabel)]));
	}

	/// <summary>
	/// Shows the result at once, as the daemon's next <c>session.changed</c> will: moved rows get the new pin, queued rows "switching to". The daemon's
	/// state replaces this; both use the daemon's labels (the Default profile is "default").
	/// </summary>
	private void Apply(ProfileSwitchResultDto result, TreeNodeViewModel node)
	{
		var tree = _tree.Current;
		var label = result.ProfileId == Constants.Profiles.DefaultProfileId ? Constants.Profiles.DefaultProfileLabel : result.ProfileName;
		if (result.NodeMoved)
			Pin(node, result.ProfileId, label);

		foreach (var moved in result.Moved)
		{
			var row = tree?.FindNode(moved.NodeId);
			if (row != null)
				Pin(row, result.ProfileId, label);
		}

		foreach (var queued in result.Queued)
		{
			var row = queued.NodeId == node.Id ? node : tree?.FindNode(queued.NodeId);
			if (row?.State != null)
				row.State = row.State with { SwitchingToProfileName = label };
		}
	}

	private static void Pin(TreeNodeViewModel row, string profileId, string label)
	{
		if (row.State != null)
			row.State = row.State with { PinnedProfileId = profileId, PinnedProfileName = label, SwitchingToProfileName = null };
	}
}
