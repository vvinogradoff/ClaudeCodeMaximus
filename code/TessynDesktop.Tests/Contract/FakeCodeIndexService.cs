using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// A code index whose build the test controls: <see cref="GetOrCreateIndexAsync"/> waits for <see cref="FinishBuild"/>, so a test can prove that a
/// request is answered while the index is still being built. Counts acquisitions and releases.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeCodeIndexService : ICodeIndexService
{
	private readonly TaskCompletionSource _build = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _acquired;
	private int _released;
	private volatile bool _ready;

	public IReadOnlyList<IndexedFileModel> Files { get; set; } = [];
	public IReadOnlyList<CodeSymbolModel> Symbols { get; set; } = [];

	public int Acquired => Volatile.Read(ref _acquired);
	public int Released => Volatile.Read(ref _released);
	public int BuildStarts { get; private set; }

	/// <summary>Makes the index ready as if another holder had built it (no acquisition by the code under test).</summary>
	public void SetReady() => _ready = true;

	public void FinishBuild() => _build.TrySetResult();

	public async Task<bool> GetOrCreateIndexAsync(string workingDirectory)
	{
		BuildStarts++;
		await _build.Task;
		_ready = true;
		Interlocked.Increment(ref _acquired);
		return true;
	}

	public void ReleaseIndex(string workingDirectory)
	{
		Interlocked.Increment(ref _released);
		_ready = false;
	}

	public IReadOnlyList<IndexedFileModel> SearchFiles(string workingDirectory, string query, int maxResults = 15) => Files;

	public IReadOnlyList<CodeSymbolModel> SearchSymbols(string workingDirectory, string query, int maxResults = 15) => Symbols;

	public bool IsIndexReady(string workingDirectory) => _ready;
}
