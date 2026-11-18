using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Turn service that records every run with its source and options and, while <see cref="Block"/> is set, holds each run until
/// <see cref="Release"/>, so a test can see that a tool returned before the turn ended (FR.15.15).
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class GatedTurnService : ISessionTurnService
{
	private readonly SemaphoreSlim _lock = new(1, 1);
	private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public List<(SessionNodeModel Node, string Prompt, TurnSource Source, TurnOptionsModel? Options)> Runs { get; } = [];

	/// <summary>Released once per run, as soon as the run has been recorded.</summary>
	public SemaphoreSlim Started { get; } = new(0);

	public bool Block { get; set; }

	public int Finished;

	public void Release() => _gate.TrySetResult();

	public SemaphoreSlim GetTurnLock(string nodeId) => _lock;

	public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, CancellationToken cancellationToken = default) =>
		RunTurnAsync(node, prompt, source, null, cancellationToken);

	public async Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, TurnOptionsModel? options, CancellationToken cancellationToken = default)
	{
		lock (Runs)
			Runs.Add((node, prompt, source, options));
		Started.Release();

		if (Block)
			await _gate.Task;

		Interlocked.Increment(ref Finished);
		return new TurnResultModel(AssistantText: "ok", SessionId: null);
	}

	public bool CancelTurn(string nodeId) => false;

	public string? ResolveProfileName(SessionNodeModel node) => node.ProfileName;
}
