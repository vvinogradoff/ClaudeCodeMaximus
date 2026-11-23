using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Switch;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An <see cref="IElevatedRunLauncher"/> that records the launch descriptions it gets and answers with a set result, or waits until the test releases it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecordingElevatedLauncher : IElevatedRunLauncher
{
	private TaskCompletionSource<ElevatedLaunchResult>? _gate;

	/// <summary>The launch descriptions started, in order.</summary>
	public List<ElevatedLaunchDto> Launches { get; } = [];

	/// <summary>What the launcher answers while it is not gated.</summary>
	public ElevatedLaunchResult Result { get; set; } = ElevatedLaunchResult.Started;

	/// <summary>From now on <see cref="LaunchAsync"/> waits for <see cref="Release"/> (the Windows prompt stays open).</summary>
	public void Gate() => _gate = new TaskCompletionSource<ElevatedLaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Answers the waiting launch.</summary>
	public void Release(ElevatedLaunchResult? result = null) => _gate!.SetResult(result ?? Result);

	public Task<ElevatedLaunchResult> LaunchAsync(ElevatedLaunchDto launch, CancellationToken cancellationToken = default)
	{
		Launches.Add(launch);
		return _gate?.Task ?? Task.FromResult(Result);
	}
}

/// <summary>An <see cref="IClipboardWriter"/> that keeps what was written.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecordingClipboard : IClipboardWriter
{
	public List<string> Texts { get; } = [];

	public bool TrySetText(string text)
	{
		Texts.Add(text);
		return true;
	}
}
