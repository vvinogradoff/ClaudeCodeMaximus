namespace ClaudeMaximus.Services;

/// <summary>
/// Resolves the display label for a Directory node from its filesystem path,
/// using the nearest .git root as the display root per FR.1.3.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IDirectoryLabelService
{
	string GetLabel(string directoryPath);
}
