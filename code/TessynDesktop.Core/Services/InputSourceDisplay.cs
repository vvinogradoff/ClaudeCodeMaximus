using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>How an input's source shows in the transcript (FR.2.6): automated inputs (scheduled, orchestrated) render teal with their own label.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class InputSourceDisplay
{
	/// <summary>True for "scheduled" (scheduled turns and mailbox deliveries) and "orchestrated" (a supervisor's prompt).</summary>
	public bool IsAutomated(string? source) =>
		source is EntrySources.Scheduled or EntrySources.Orchestrated;

	/// <summary>The header label of an input: SCHEDULED or SUPERVISOR for an automated one, "You" for any other.</summary>
	public string Label(string? source) => source switch
	{
		EntrySources.Scheduled    => Constants.InputNav.LabelScheduled,
		EntrySources.Orchestrated => Constants.InputNav.LabelSupervisor,
		_                         => Constants.InputNav.LabelUser,
	};
}
