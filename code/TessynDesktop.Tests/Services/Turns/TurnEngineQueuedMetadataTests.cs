using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// The one deliberate difference to the former <c>TurnController</c> (P1.I stage 1, see the report): the model and effort stamped on a turn's
/// first assistant entry belong to that turn. The controller kept them in two fields shared by all its sends, so with a queued send the
/// running turn's answer was stamped with the queued send's choice and the queued turn's answer got none.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEngineQueuedMetadataTests : IDisposable
{
	private readonly TurnEngineHarness _h = new();

	public void Dispose() => _h.Dispose();

	[Fact]
	public async Task QueuedSends_EachStampTheirOwnModelAndEffortOnTheirFirstAssistantEntry()
	{
		var node = TurnEngineHarness.Node();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_h.Process.Script(async (_, onEvent, _) =>
		{
			await gate.Task;
			onEvent(Evt.Assistant("first answer"));
		});
		_h.Process.ScriptEvents(Evt.Assistant("second answer"));

		var first = _h.Run(node, "one", settings: TurnEngineHarness.Chosen(model: "opus", effort: "high"));
		await Waits.Until(() => _h.Process.Calls.Count == 1);
		var second = _h.Run(node, "two", settings: TurnEngineHarness.Chosen(model: "sonnet", effort: "low"));

		gate.SetResult();
		await first.Within();
		await second.Within();

		var answers = _h.Files.Entries.Where(e => e.Role == Constants.SessionFile.RoleAssistant).ToList();
		Assert.Equal(("opus", "high"), (answers[0].ModelId, answers[0].Effort));
		Assert.Equal(("sonnet", "low"), (answers[1].ModelId, answers[1].Effort));
	}
}
