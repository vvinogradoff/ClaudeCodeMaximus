namespace TessynDesktop.Models;

/// <summary>What a client starts for an approved run (FR.15.19): built by the daemon from the stored request.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevatedRunLaunchModel(
	string RequestId,
	string FileName,
	string Arguments,
	string WorkingDirectory,
	string RunDir,
	string Verb,
	int StartTimeoutSeconds);
