using System.Text;
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
public sealed class ProfileSwitchViewModel
{
	/// <summary>Upper bound of descendants read, so a runaway tree cannot make the dialog hang.</summary>
	private const int MaxDescendants = 500;

	private readonly IDialogService _dialogs;
	private readonly TreeContext _tree;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<ProfileSwitchViewModel> _logger;
	private ITessynClient? _client;

	public ProfileSwitchViewModel(IDialogService dialogs, TreeContext tree, IUiDispatcher ui, ILogger<ProfileSwitchViewModel> logger)
	{
		_dialogs = dialogs;
		_tree = tree;
		_ui = ui;
		_logger = logger;
	}

	public void Attach(ITessynClient client) => _client = client;

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
		var client = _client;
		if (client == null || !CanSwitch(node))
			return;

		try
		{
			var profileId = await PickProfileAsync(client, node!);
			if (profileId == null)
				return;

			var descendants = await ReadDescendantsAsync(client, node!.Id);
			var includeDescendants = false;
			if (descendants.Count > 0)
			{
				var answer = await AskCascadeAsync(node, profileId, descendants, client);
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
			if (summary.NeedsAttention)
				await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
					Constants.Profiles.ResultTitle, summary.ToText(node.Label), Constants.Profiles.ResultOk, Constants.DialogTexts.Cancel));
		}
		catch (Exception ex)
		{
			LastError = ex.Message;
			_logger.LogWarning(ex, "Switching the profile of {NodeId} failed", node!.Id);
		}
	}

	private async Task<string?> PickProfileAsync(ITessynClient client, TreeNodeViewModel node)
	{
		var profiles = (await client.ProfileListAsync()).Profiles;
		var current = node.State?.ProfileName;
		var choices = profiles
			.Where(p => !string.Equals(p.DisplayName, current, StringComparison.OrdinalIgnoreCase))
			.Select(p => new DialogChoice(p.Id, p.AuthState == ProfileAuthState.LoggedOut ? $"{p.DisplayName} ({Constants.Profiles.AuthStateLoggedOut})" : p.DisplayName))
			.ToList();
		if (choices.Count == 0)
			return null;

		return await _dialogs.ChoiceAsync(new ChoiceDialogRequest(Constants.Profiles.PickTitle, string.Format(Constants.Profiles.PickMessage, node.Label), choices));
	}

	/// <summary>All descendants of the session through <c>tree.workers</c>, breadth first.</summary>
	private static async Task<IReadOnlyList<TreeSessionDto>> ReadDescendantsAsync(ITessynClient client, string nodeId)
	{
		var found = new List<TreeSessionDto>();
		var seen = new HashSet<string> { nodeId };
		var queue = new Queue<string>();
		queue.Enqueue(nodeId);
		while (queue.Count > 0 && found.Count < MaxDescendants)
		{
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

		return found;
	}

	private async Task<string?> AskCascadeAsync(TreeNodeViewModel node, string profileId, IReadOnlyList<TreeSessionDto> descendants, ITessynClient client)
	{
		var target = (await client.ProfileListAsync()).Profiles.FirstOrDefault(p => p.Id == profileId)?.DisplayName ?? profileId;
		var message = new StringBuilder();
		message.AppendLine(string.Format(Constants.Profiles.CascadeMessage, node.Label, descendants.Count, target));
		foreach (var worker in descendants.Take(Constants.Profiles.MaxListedDescendants))
		{
			var on = worker.State.ProfileName ?? "?";
			message.AppendLine("  • " + string.Format(worker.State.Running ? Constants.Profiles.DescendantRunning : Constants.Profiles.DescendantIdle, worker.Name, on));
		}

		if (descendants.Count > Constants.Profiles.MaxListedDescendants)
			message.AppendLine("  " + string.Format(Constants.Profiles.DescendantMore, descendants.Count - Constants.Profiles.MaxListedDescendants));

		return await _dialogs.ChoiceAsync(new ChoiceDialogRequest(
			Constants.Profiles.CascadeTitle,
			message.ToString().TrimEnd(),
			[new DialogChoice(Constants.Profiles.CascadeAllId, Constants.Profiles.CascadeAllLabel), new DialogChoice(Constants.Profiles.CascadeOnlyId, Constants.Profiles.CascadeOnlyLabel)]));
	}

	/// <summary>Shows the new profile on the rows that were moved now; queued and failed ones keep their old badge until their turn.</summary>
	private void Apply(ProfileSwitchResultDto result, TreeNodeViewModel node)
	{
		var tree = _tree.Current;
		if (result.NodeMoved)
			SetProfile(node, result.ProfileName);

		foreach (var moved in result.Moved)
		{
			var row = tree?.FindNode(moved.NodeId);
			if (row != null)
				SetProfile(row, result.ProfileName);
		}
	}

	private static void SetProfile(TreeNodeViewModel row, string profileName)
	{
		if (row.State != null)
			row.State = row.State with { ProfileName = profileName };
	}
}
