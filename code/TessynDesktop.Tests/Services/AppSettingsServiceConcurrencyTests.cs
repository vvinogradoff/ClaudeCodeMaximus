using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// ISSUE concurrent-settings-save-lost-update: <see cref="AppSettingsService.Save"/> used to be a
/// completely unsynchronized full-graph serialize + atomic rename. Two concurrent Saves could race —
/// a Save that serialized a stale snapshot could have its rename land last and silently overwrite a
/// newer version that included a just-added session node, with no exception. These tests reproduce
/// the lost-update pattern (many threads mutating <see cref="AppSettingsModel.Tree"/> and Saving
/// under <see cref="IAppSettingsService.SyncRoot"/>, mirroring how <c>SessionRegistry</c> does it)
/// and prove every mutation survives a fresh <see cref="AppSettingsService.Load"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AppSettingsServiceConcurrencyTests : IDisposable
{
	private readonly string _tempDir;

	public AppSettingsServiceConcurrencyTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cmx_concurrency_test_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	[Fact]
	public async Task Save_ManyConcurrentThreads_NoLostUpdates()
	{
		const int threadCount = 8;
		const int perThread   = 50;

		var svc = new AppSettingsService(_tempDir);
		var expectedPaths = new ConcurrentBag<string>();

		var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(() =>
		{
			for (var i = 0; i < perThread; i++)
			{
				var path = $@"C:\dir-{threadId}-{i}";
				lock (svc.SyncRoot)
				{
					svc.Settings.Tree.Add(new DirectoryNodeModel { Path = path });
					svc.Save();
				}
				expectedPaths.Add(path);
			}
		})).ToArray();

		await Task.WhenAll(tasks);

		Assert.Equal(threadCount * perThread, expectedPaths.Count);

		// Fresh instance reading from disk — proves the LAST Save actually persisted everything,
		// not just that the in-memory Settings.Tree (shared with svc) happens to have it all.
		var reloaded = new AppSettingsService(_tempDir);
		reloaded.Load();

		Assert.Equal(expectedPaths.Count, reloaded.Settings.Tree.Count);
		var reloadedPaths = reloaded.Settings.Tree.Select(d => d.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var expected in expectedPaths)
			Assert.Contains(expected, reloadedPaths);
	}

	[Fact]
	public async Task Save_ConcurrentMutateAndRedundantSaves_NeverThrows()
	{
		// One mutator thread adds nodes (mirroring SessionRegistry.CreateSessionForAgent) while
		// several other threads repeatedly call Save() with no mutation of their own (mirroring
		// SessionTurnService's per-turn Save and the streaming event-reader thread) — all under the
		// shared lock. Without synchronization, JsonSerializer enumerating Settings.Tree in one
		// thread's Save while another thread concurrently mutates it can throw
		// InvalidOperationException ("Collection was modified..."); with the lock, it never can.
		const int redundantSaverThreads = 6;
		const int iterations           = 60;

		var svc        = new AppSettingsService(_tempDir);
		var exceptions = new ConcurrentBag<Exception>();

		var mutator = Task.Run(() =>
		{
			try
			{
				for (var i = 0; i < iterations; i++)
				{
					lock (svc.SyncRoot)
					{
						svc.Settings.Tree.Add(new DirectoryNodeModel { Path = $@"C:\mutate-{i}" });
						svc.Save();
					}
				}
			}
			catch (Exception ex) { exceptions.Add(ex); }
		});

		var savers = Enumerable.Range(0, redundantSaverThreads).Select(_ => Task.Run(() =>
		{
			try
			{
				for (var i = 0; i < iterations; i++)
				{
					lock (svc.SyncRoot)
						svc.Save();
				}
			}
			catch (Exception ex) { exceptions.Add(ex); }
		})).ToArray();

		await Task.WhenAll(savers.Append(mutator));

		Assert.Empty(exceptions);
		Assert.Equal(iterations, svc.Settings.Tree.Count);
	}

	[Fact]
	public async Task Save_BareConcurrentCallsWithNoCallerSideLock_NoTornTempFileAndLastWritePerThreadSurvives()
	{
		// Unlike the two tests above (which mirror SessionRegistry's lock(SyncRoot) { mutate; Save(); }
		// pattern), this mirrors the OTHER class of real caller that motivated locking inside Save()
		// itself: SessionTurnService and various UI toggles call _appSettings.Save() bare, with no
		// lock at the call site at all. Each thread here mutates only its OWN pre-allocated node (no
		// shared List<T> mutation race to conflate with) and calls Save() with zero external locking —
		// isolating whether Save() is safe to call concurrently on its own. Without Save()'s internal
		// lock, two threads' File.WriteAllText/File.Move against the identical ".tmp" path race and
		// reliably throw (IOException/UnauthorizedAccessException) rather than silently losing data.
		const int threadCount = 8;
		const int perThread   = 30;

		var svc = new AppSettingsService(_tempDir);
		var dirs = Enumerable.Range(0, threadCount)
			.Select(i => new DirectoryNodeModel { Path = $@"C:\bare-{i}" })
			.ToList();
		svc.Settings.Tree.AddRange(dirs);
		svc.Save();

		var exceptions = new ConcurrentBag<Exception>();

		var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(() =>
		{
			try
			{
				for (var i = 0; i < perThread; i++)
				{
					dirs[threadId].SelectedModelId = $"model-{threadId}-{i}";
					svc.Save();
				}
			}
			catch (Exception ex) { exceptions.Add(ex); }
		})).ToArray();

		await Task.WhenAll(tasks);

		Assert.Empty(exceptions);

		var reloaded = new AppSettingsService(_tempDir);
		reloaded.Load();
		for (var t = 0; t < threadCount; t++)
			Assert.Equal($"model-{t}-{perThread - 1}", reloaded.Settings.Tree[t].SelectedModelId);
	}
}
