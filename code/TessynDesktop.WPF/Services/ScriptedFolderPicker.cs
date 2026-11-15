using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The folder picker of <c>--test-window</c> runs: no dialog opens, each pick is answered in order from the <c>--test-folder</c> entries and logged;
/// once they are used up the pick is cancelled.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScriptedFolderPicker : IFolderPicker
{
	private readonly Queue<string> _script;
	private readonly ILogger<ScriptedFolderPicker> _logger;

	public ScriptedFolderPicker(IEnumerable<string> script, ILogger<ScriptedFolderPicker> logger)
	{
		_script = new Queue<string>(script);
		_logger = logger;
	}

	public Task<string?> PickAsync(string title)
	{
		string? answer;
		lock (_script)
			answer = _script.Count > 0 ? _script.Dequeue() : null;

		_logger.LogInformation("Test window: folder dialog '{Title}' answered with {Answer}", title, answer ?? "cancel (no script left)");
		return Task.FromResult(answer);
	}
}
