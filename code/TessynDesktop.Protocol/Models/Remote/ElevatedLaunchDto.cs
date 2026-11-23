namespace TessynDesktop.Models.Remote;

/// <summary>
/// What a client starts after <c>elevated.approve</c> (tdcp-spec 10.A13), built by the daemon from the stored request: the program, its arguments, the
/// working directory, the run folder and the ShellExecute verb. The client starts exactly that and changes nothing.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevatedLaunchDto(
	string RequestId,
	string FileName,
	string Arguments,
	string WorkingDirectory,
	string RunDir,
	string Verb,
	int StartTimeoutSeconds);
