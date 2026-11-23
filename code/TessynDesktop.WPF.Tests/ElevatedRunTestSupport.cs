using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>Builders and the Focus view model around an elevated-run card (TC.ER.51 to TC.ER.55, FR.23.32).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ElevatedRunFixture
{
	public static readonly DateTimeOffset T0 = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

	public const string Lead = "lead-node-0001";
	public const string Worker = "wrk-node-0002";

	public FakeTessynClient Client { get; } = new();
	public RecordingElevatedLauncher Launcher { get; } = new();
	public RecordingClipboard Clipboard { get; } = new();
	public TickingTimeProvider Time { get; } = new(T0.AddMinutes(1));

	public FocusViewModel CreateFocus() => new(Client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance, Launcher, Clipboard, Time);

	public static ElevatedRunDto Run(ElevatedRunStatus status = ElevatedRunStatus.Pending, string id = "er-1", string command = "cmd /c echo hi & exit 3", string requester = Lead, string name = "Lead") => new()
	{
		Id = id,
		Status = status,
		Requester = new ElevatedRequesterDto(requester, name, requester[..8]),
		Reason = "Run the suite as the test user",
		WorkingDir = @"C:\Projects\Personal\Tessyn-wt-elevruns",
		Command = command,
		TimeoutMinutes = 60,
		CreatedUtc = T0,
		ExpiresUtc = T0.AddMinutes(30),
		LogPath = @"C:\Data\elevated-runs\" + id + @"\output.log",
	};

	public static AttentionItemDto Item(ElevatedRunDto run, int number = 1, AttentionStatus status = AttentionStatus.Open) => new()
	{
		Id = "A" + number,
		Number = number,
		Kind = AttentionKind.Ask,
		Status = status,
		Title = "Run as administrator: " + run.Command[..Math.Min(120, run.Command.Length)],
		Body = "Reason: " + run.Reason + "\nWorking directory: " + run.WorkingDir + "\n\n```\n" + run.Command + "\n```",
		CreatedUtc = T0.AddMinutes(number),
		UpdatedUtc = T0.AddMinutes(number),
		Action = new AttentionActionDto(RemoteProtocol.ElevatedRuns.ActionType, run),
	};

	/// <summary>Puts the run on the fake daemon and its item in the ledger of <paramref name="nodeId"/>.</summary>
	public AttentionItemDto Seed(ElevatedRunDto run, string nodeId = Lead, int number = 1, AttentionStatus status = AttentionStatus.Open)
	{
		Client.PutElevatedRun(run);
		var item = Item(run, number, status);
		if (!Client.Attention.TryGetValue(nodeId, out var list))
			Client.Attention[nodeId] = list = [];
		list.Add(item);
		return item;
	}

	/// <summary>The card of the only open item, after the view model loaded the session.</summary>
	public async Task<(FocusViewModel Focus, ElevatedRunCardViewModel Card)> ShowAsync(ElevatedRunDto run, string nodeId = Lead)
	{
		Seed(run, nodeId);
		var focus = CreateFocus();
		await focus.SetSessionAsync(nodeId);
		return (focus, focus.Items.Single().Elevated!);
	}
}
