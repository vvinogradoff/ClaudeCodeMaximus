using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The dialog presenter of <c>--test-window</c> runs: no window opens (a real modal one would wait for a click forever), each dialog is answered from a
/// script and logged. Script entries (repeatable <c>--test-dialog</c>): <c>accept</c>, <c>cancel</c>, <c>text:&lt;value&gt;</c> (accepts an input dialog with that text),
/// <c>choice:&lt;id&gt;</c>. Once the script is used up, or an entry does not fit the dialog, the dialog is cancelled.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScriptedDialogPresenter : IDialogPresenter
{
	private readonly Queue<string> _script;
	private readonly ILogger<ScriptedDialogPresenter> _logger;

	public ScriptedDialogPresenter(IEnumerable<string> script, ILogger<ScriptedDialogPresenter> logger)
	{
		_script = new Queue<string>(script);
		_logger = logger;
	}

	public async Task<DialogOutcome> PresentAsync(DialogViewModel dialog)
	{
		string? step;
		lock (_script)
			step = _script.Count > 0 ? _script.Dequeue() : null;

		_logger.LogInformation("Test window: dialog '{Title}' ({Kind}) answered with {Step}", dialog.Title, dialog.Kind, step ?? "cancel (no script left)");
		Apply(dialog, step);
		if (!dialog.Outcome.IsCompleted)
			dialog.CancelCommand.Execute(null);

		return await dialog.Outcome.ConfigureAwait(false);
	}

	private static void Apply(DialogViewModel dialog, string? step)
	{
		if (step == null || step.Equals("cancel", StringComparison.OrdinalIgnoreCase))
			return;

		if (step.Equals("accept", StringComparison.OrdinalIgnoreCase))
		{
			dialog.AcceptCommand.Execute(null);
		}
		else if (step.StartsWith("text:", StringComparison.OrdinalIgnoreCase) && dialog.IsInput)
		{
			dialog.InputText = step["text:".Length..];
			dialog.AcceptCommand.Execute(null);
		}
		else if (step.StartsWith("choice:", StringComparison.OrdinalIgnoreCase) && dialog.IsChoice)
		{
			var choice = dialog.Choices.FirstOrDefault(c => c.Id == step["choice:".Length..]);
			if (choice != null)
				dialog.ChooseCommand.Execute(choice);
		}
	}
}
