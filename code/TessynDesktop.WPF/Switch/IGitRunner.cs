namespace TessynDesktop.WPF.Switch;

/// <summary>Runs git in a folder (FR.32.9); behind an interface so the failure paths can be faked.</summary>
/// <remarks>Created by Claude</remarks>
public interface IGitRunner
{
	GitResult Run(string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout);
}
