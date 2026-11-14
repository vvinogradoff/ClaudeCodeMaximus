using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Services;
using Xunit;
using Xunit.Abstractions;

namespace TessynDesktop.Tests.Services;

/// <summary>Concurrent append and read, and the tail-page timing against a full parse (P1.E).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionContentStoreConcurrencyTests(ITestOutputHelper output) : ContentStoreTestBase
{
	private const string File1 = "a.txt";

	[Fact]
	public async Task ConcurrentAppendAndRead_NeverSeesATornOrInconsistentPage()
	{
		var file = Files.CreateSessionFile();
		using var store = NewStore();
		const int appends = 400;
		var failures = new ConcurrentQueue<string>();
		var done = false;

		var writer = Task.Run(() =>
		{
			for (var i = 0; i < appends; i++)
			{
				Files.AppendMessage(file, i % 2 == 0 ? Constants.SessionFile.RoleUser : Constants.SessionFile.RoleAssistant, $"m{i}");
				if (i % 50 == 0)
					Thread.Yield();
			}

			Volatile.Write(ref done, true);
		});

		void Reader()
		{
			var lastTotal = 0;
			while (!Volatile.Read(ref done))
			{
				try
				{
					var page = store.ReadPage(file, new(null, null, 20));
					if (page.Total < lastTotal)
						failures.Enqueue($"total went back {lastTotal} -> {page.Total}");

					lastTotal = page.Total;
					foreach (var entry in page.Entries)
					{
						if (entry.Entry.Content != $"m{entry.Ordinal}")
							failures.Enqueue($"ordinal {entry.Ordinal} holds {entry.Entry.Content}");
					}

					var header = store.GetHeader(file);
					if (header.EntryCount < page.Total)
						failures.Enqueue($"header {header.EntryCount} behind page {page.Total}");
				}
				catch (Exception ex)
				{
					failures.Enqueue(ex.ToString());
				}
			}
		}

		var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(Reader)).ToArray();
		await writer;
		await Task.WhenAll(readers);

		Assert.Empty(failures);
		Assert.Equal(appends, store.GetHeader(file).EntryCount);
		AssertSameEntries(Files.ReadEntries(file), ReadAllFull(store, file), 0);
	}

	[Fact]
	public async Task ConcurrentAppendsFromTwoWriters_AreAllIndexed()
	{
		var first = Files.CreateSessionFile();
		var second = Files.CreateSessionFile();
		using var store = NewStore();
		store.GetHeader(first);
		store.GetHeader(second);

		await Task.WhenAll(
			Task.Run(() => { for (var i = 0; i < 150; i++) Files.AppendMessage(first, "USER", $"m{i}"); }),
			Task.Run(() => { for (var i = 0; i < 150; i++) Files.AppendMessage(second, "USER", $"m{i}"); }),
			Task.Run(() => { for (var i = 0; i < 300; i++) { store.GetHeader(first); store.GetHeader(second); } }));

		Assert.Equal(150, store.GetHeader(first).EntryCount);
		Assert.Equal(150, store.GetHeader(second).EntryCount);
	}

	[Fact]
	public async Task Rewrite_DuringReads_NeverThrowsOutOfTheStore()
	{
		WriteConversation(File1, 50);
		using var store = NewStore();
		var failures = new ConcurrentQueue<string>();
		var done = false;

		var reader = Task.Run(() =>
		{
			while (!Volatile.Read(ref done))
			{
				try
				{
					var page = store.ReadPage(File1, new(null, null, 10));
					foreach (var entry in page.Entries)
					{
						if (!entry.Entry.Content.StartsWith('m') && !entry.Entry.Content.StartsWith('r'))
							failures.Enqueue("unexpected content " + entry.Entry.Content);
					}
				}
				catch (TdcpException)
				{
					// "Session history changed" is the contract's answer when a rewrite wins every retry.
				}
				catch (Exception ex)
				{
					failures.Enqueue(ex.ToString());
				}
			}
		});

		for (var round = 0; round < 20; round++)
		{
			var entries = Files.ReadEntries(File1).Take(20 + round).ToList();
			RewriteWithRetry(entries);
			Files.AppendMessage(File1, "USER", $"r{round}");
		}

		Volatile.Write(ref done, true);
		await reader;

		Assert.Empty(failures);
		AssertSameEntries(Files.ReadEntries(File1), ReadAllFull(store, File1), 0);
	}

	/// <summary>
	/// Windows refuses to replace a file that any reader has open, even one sharing delete; a writer racing readers retries.
	/// This holds for every reader of session files, the existing ones included.
	/// </summary>
	private void RewriteWithRetry(System.Collections.Generic.IReadOnlyList<TessynDesktop.Models.SessionEntryModel> entries)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				Files.WriteSessionFile(File1, entries);
				return;
			}
			catch (UnauthorizedAccessException) when (attempt < 200)
			{
				Thread.Sleep(2);
			}
		}
	}

	[Fact]
	public void Timing_TailPageOf100FromAnIndexedFileVersusAFullParse()
	{
		const int entries = 1600;
		var sb = new StringBuilder();
		for (var i = 0; i < entries; i++)
		{
			var body = string.Join('\n', Enumerable.Range(0, 12 + i % 30).Select(n => $"line {n} of entry {i}: the quick brown fox jumps over the lazy dog"));
			sb.Append(i % 2 == 0 ? EntryText("USER", i, body, "profile=\"work\"") : EntryText("ASSISTANT", i, body, "model=\"opus\" effort=\"high\""));
		}

		WriteRaw(File1, sb.ToString());
		var size = new System.IO.FileInfo(FullPath(File1)).Length;

		using var store = NewStore();
		var build = Stopwatch.StartNew();
		store.GetHeader(File1);
		build.Stop();

		const int rounds = 30;
		store.ReadPage(File1, new(null, null, 100));
		var tail = Stopwatch.StartNew();
		for (var i = 0; i < rounds; i++)
			Assert.Equal(100, store.ReadPage(File1, new(null, null, 100)).Entries.Count);
		tail.Stop();

		Files.ReadEntries(File1);
		var parse = Stopwatch.StartNew();
		for (var i = 0; i < rounds; i++)
			Assert.Equal(entries, Files.ReadEntries(File1).Count);
		parse.Stop();

		var header = Stopwatch.StartNew();
		for (var i = 0; i < 1000; i++)
			store.GetHeader(File1);
		header.Stop();

		output.WriteLine($"file: {entries} entries, {size / 1024} KB");
		output.WriteLine($"cold index build:             {build.Elapsed.TotalMilliseconds,8:F2} ms");
		output.WriteLine($"tail page of 100 (indexed):   {tail.Elapsed.TotalMilliseconds / rounds,8:F2} ms avg of {rounds}");
		output.WriteLine($"full parse of the file:       {parse.Elapsed.TotalMilliseconds / rounds,8:F2} ms avg of {rounds}");
		output.WriteLine($"header (O(1), indexed):       {header.Elapsed.TotalMilliseconds / 1000,8:F4} ms avg of 1000");
	}
}
