using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary>The file picker of <c>--test-window</c> runs: no dialog opens; the pick is logged and cancelled.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScriptedFilePicker : IFilePicker
{
	private readonly ILogger<ScriptedFilePicker> _logger;

	public ScriptedFilePicker(ILogger<ScriptedFilePicker> logger)
	{
		_logger = logger;
	}

	public Task<string?> PickAsync(string title, string filter, string? initialPath = null)
	{
		_logger.LogInformation("Test window: file dialog '{Title}' cancelled (no dialog opens)", title);
		return Task.FromResult<string?>(null);
	}
}
