using System;
using System.IO;
using System.Reflection;
using System.Threading;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>P1.E review follow-ups: the append handler (B1), replacing a file a reader holds open (B2), orphaned sidecars (B3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionContentStoreReviewFixTests : ContentStoreTestBase
{
	// ── B1 ────────────────────────────────────────────────────────────────────────

	[Fact]
	public void B1_An_exception_inside_the_eager_index_update_never_reaches_the_appender()
	{
		var file = Files.CreateSessionFile();
		Files.AppendMessage(file, Constants.SessionFile.RoleUser, "one");
		var proxy = FaultyFiles.Create(Files);
		using var store = new SessionContentStore(proxy, IndexDirectory);
		Assert.Equal(1, store.GetHeader(file).EntryCount); // loads the index, so appends are tracked eagerly

		var handler = (FaultyFiles)(object)proxy;
		handler.FailStamp = true;
		Files.AppendMessage(file, Constants.SessionFile.RoleAssistant, "two"); // must not throw
		Assert.True(SpinWait.SpinUntil(() => handler.FailuresThrown > 0, TimeSpan.FromSeconds(5)), "the background update never ran");

		handler.FailStamp = false;
		Assert.Equal(2, store.GetHeader(file).EntryCount);
		Assert.Equal("two", store.ReadEntry(file, 1)!.Entry.Content);
	}

	[Fact]
	public void B1_The_appender_does_not_wait_for_the_index_update()
	{
		var file = Files.CreateSessionFile();
		Files.AppendMessage(file, Constants.SessionFile.RoleUser, "one");
		var proxy = FaultyFiles.Create(Files);
		var handler = (FaultyFiles)(object)proxy;
		using var store = new SessionContentStore(proxy, IndexDirectory);
		store.GetHeader(file);

		handler.TestThreadId = Environment.CurrentManagedThreadId;
		handler.BlockOtherThreads.Reset();
		try
		{
			var started = DateTime.UtcNow;
			Files.AppendMessage(file, Constants.SessionFile.RoleAssistant, "two");

			Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2), "the append waited for the index update");
			Assert.True(SpinWait.SpinUntil(() => handler.BlockedCalls > 0, TimeSpan.FromSeconds(5)), "the update was not run off the appender thread");
		}
		finally
		{
			handler.BlockOtherThreads.Set();
		}

		Assert.Equal(2, store.GetHeader(file).EntryCount);
	}

	// ── B2 ────────────────────────────────────────────────────────────────────────

	[Fact]
	public void B2_RewriteSessionFile_waits_out_a_reader_that_briefly_holds_the_file()
	{
		var file = WriteConversation("rewrite.txt", 2);
		var text = EntryText("USER", 5, "fresh");
		var reader = new FileStream(FullPath(file), FileMode.Open, FileAccess.Read, FileShare.Read);
		var release = new Thread(() =>
		{
			Thread.Sleep(150);
			reader.Dispose();
		});
		release.Start();

		Files.RewriteSessionFile(file, text);
		release.Join();

		Assert.Equal(text, File.ReadAllText(FullPath(file)).TrimStart('﻿'));
	}

	[Fact]
	public void B2_WriteSessionFile_waits_out_a_reader_that_briefly_holds_the_file()
	{
		var file = WriteConversation("write.txt", 2);
		var entries = Files.ReadEntries(file);
		var reader = new FileStream(FullPath(file), FileMode.Open, FileAccess.Read, FileShare.Read);
		var release = new Thread(() =>
		{
			Thread.Sleep(150);
			reader.Dispose();
		});
		release.Start();

		Files.WriteSessionFile(file, [entries[0]]);
		release.Join();

		Assert.Single(Files.ReadEntries(file));
	}

	// ── B3 ────────────────────────────────────────────────────────────────────────

	[Fact]
	public void B3_Deleting_the_session_file_removes_its_sidecar_and_temp_file()
	{
		var file = WriteConversation("gone.txt", 3);
		using var store = NewStore();
		store.GetHeader(file);
		var sidecar = Assert.Single(SidecarFiles());
		File.WriteAllText(sidecar + Constants.ContentStore.TempFileExtension, "half written");

		Files.DeleteSessionFile(file);

		Assert.Empty(SidecarFiles());
	}

	[Fact]
	public void B3_Finding_the_session_file_missing_removes_its_sidecar()
	{
		var file = WriteConversation("vanished.txt", 3);
		using var store = NewStore();
		store.GetHeader(file);
		Assert.Single(SidecarFiles());

		File.Delete(FullPath(file)); // behind the service's back: no event
		Assert.False(store.GetHeader(file).Exists);

		Assert.Empty(SidecarFiles());
	}

	/// <summary>Forwards to the real service; can fail or block <c>GetFileStamp</c> for calls from threads other than the test's.</summary>
	public class FaultyFiles : DispatchProxy
	{
		private ISessionFileService _inner = null!;
		private int _failuresThrown;
		private int _blockedCalls;

		public volatile bool FailStamp;
		public int TestThreadId;
		public ManualResetEventSlim BlockOtherThreads { get; } = new(initialState: true);
		public int FailuresThrown => Volatile.Read(ref _failuresThrown);
		public int BlockedCalls => Volatile.Read(ref _blockedCalls);

		public static ISessionFileService Create(ISessionFileService inner)
		{
			var proxy = DispatchProxy.Create<ISessionFileService, FaultyFiles>();
			((FaultyFiles)(object)proxy)._inner = inner;
			return proxy;
		}

		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			if (targetMethod!.Name == nameof(ISessionFileService.GetFileStamp) && Environment.CurrentManagedThreadId != TestThreadId)
			{
				if (!BlockOtherThreads.IsSet)
				{
					Interlocked.Increment(ref _blockedCalls);
					BlockOtherThreads.Wait();
				}

				if (FailStamp)
				{
					Interlocked.Increment(ref _failuresThrown);
					throw new InvalidOperationException("injected failure");
				}
			}

			try
			{
				return targetMethod.Invoke(_inner, args);
			}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
			{
				throw ex.InnerException;
			}
		}
	}
}
