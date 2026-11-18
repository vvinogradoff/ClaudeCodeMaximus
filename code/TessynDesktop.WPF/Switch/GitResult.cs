namespace TessynDesktop.WPF.Switch;

/// <summary>What one git run did (FR.32.9): it did not start, ran out of time, or ended with an exit code and its standard output.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record GitResult(bool Started, bool TimedOut, int ExitCode, string Output)
{
	public static readonly GitResult NotAvailable = new(false, false, -1, "");

	public static readonly GitResult OutOfTime = new(true, true, -1, "");

	/// <summary>True when git ran to its end (any exit code).</summary>
	public bool Completed => Started && !TimedOut;
}
