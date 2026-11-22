using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The session module over a real session file service and a real content store in a temp directory (P1.F / P1.G tests). A
/// second, store-less module over the same files is the legacy reference. <see cref="Restart"/> drops the store, reader and
/// module and builds new ones over the same files and sidecars: a daemon restart.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class EntryIdsEnv : IDisposable
{
	public static readonly DateTimeOffset Start = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

	public string Root { get; }
	public string IndexDirectory { get; }
	public AppSettingsService Settings { get; }
	public SessionFileService Files { get; }
	public ISessionFileService CountedFiles { get; }
	public RecordingProxy<ISessionFileService> FileCalls { get; }
	public SessionRegistry Registry { get; }
	public DirectoryNodeModel Directory { get; }
	public SessionNodeModel Session { get; }

	public SessionContentStore Store { get; private set; } = null!;
	public SessionEpochTracker Epochs { get; private set; } = null!;
	public SessionEntryReader Reader { get; private set; } = null!;
	/// <summary>The tags every session of the module reports (empty unless a test fills it).</summary>
	public StubTagService TagService { get; } = new();

	public SessionRpcModule Module { get; private set; } = null!;
	public TdcpDispatcher Dispatcher { get; private set; } = null!;

	/// <summary>The same module over a reader without a content store: the pre-P1.F behaviour on the same files.</summary>
	public SessionRpcModule LegacyModule { get; private set; } = null!;

	public TdcpDispatcher LegacyDispatcher { get; private set; } = null!;

	public FakeModelServiceForTurnService Models { get; } = new();
	public FakeProfileServiceForTurnService ProfileService { get; } = new();

	public EntryIdsEnv()
	{
		Root           = Path.Combine(Path.GetTempPath(), "cm_test_entry_ids_" + Path.GetRandomFileName());
		IndexDirectory = Path.Combine(Root, "entry-index");
		System.IO.Directory.CreateDirectory(Root);

		Settings = new AppSettingsService(Root);
		Settings.Settings.SessionFilesRoot = Root;
		Files = new SessionFileService(Settings);

		CountedFiles = RecordingProxy<ISessionFileService>.Wrap(Files, out var recorder);
		FileCalls = recorder;

		Registry  = new SessionRegistry(Settings, Files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		Directory = Registry.AddDirectory(Path.Combine(Root, "proj"));
		Session   = Registry.AddSession(Directory, "Main session");

		Build();
	}

	public string FileName => Session.FileName;

	public string FullPath => Files.GetFullPath(FileName);

	/// <summary>Drops store, reader and module and builds new ones over the same files and sidecars.</summary>
	public void Restart()
	{
		Store.Dispose();
		Epochs.Dispose();
		Build();
	}

	private void Build()
	{
		Epochs = new SessionEpochTracker(Files);
		Store  = new SessionContentStore(CountedFiles, IndexDirectory);
		Reader = new SessionEntryReader(CountedFiles, Epochs, Store);

		Module     = NewModule(Reader);
		Dispatcher = new TdcpDispatcher([Module]);

		LegacyModule     = NewModule(new SessionEntryReader(Files, Epochs));
		LegacyDispatcher = new TdcpDispatcher([LegacyModule]);
	}

	private SessionRpcModule NewModule(ISessionEntryReader reader)
	{
		var streaming = new StubStreamingManager();
		var turns     = new StubTurnService();
		var attention = new FakeAttentionService();
		var labels    = new ProfileLabelService(ProfileService, Settings);
		var options   = new TurnOptionsResolver(Models, Settings, labels);

		return new SessionRpcModule(
			Registry, Settings,
			new SessionStateBuilder(Registry, TestEngines.Claude(streaming), attention, new FakeActivityService()),
			TagService, attention, reader, Epochs, Models, labels,
			new SessionSendService(turns, Registry), options, turns, TestEngines.Claude(streaming));
	}

	// ── transcript helpers ───────────────────────────────────────────────────────────────────

	/// <summary>The text of one entry in the on-disk format, with a fixed timestamp.</summary>
	public static string EntryText(string role, int minute, string body, string? meta = null) =>
		$"[{Start.AddMinutes(minute).ToString(Constants.SessionFile.TimestampFormat)}] {role}{(meta == null ? "" : " " + meta)}\n{body}\n\n";

	/// <summary>Entry <paramref name="i"/> of the alternating user/assistant conversation whose bodies are <c>m{i}</c>.</summary>
	public static string ConversationEntry(int i) => i % 2 == 0
		? EntryText("USER", i, $"m{i}", "profile=\"work\"")
		: EntryText("ASSISTANT", i, $"m{i}", "model=\"opus\" effort=\"high\"");

	/// <summary>Replaces the file with <paramref name="count"/> conversation entries (UTF-8 with a byte order mark, as the app writes).</summary>
	public void WriteConversation(int count)
	{
		var sb = new StringBuilder();
		for (var i = 0; i < count; i++)
			sb.Append(ConversationEntry(i));

		WriteRaw(sb.ToString());
	}

	public void WriteRaw(string text) => File.WriteAllText(FullPath, text, new UTF8Encoding(true));

	public void AppendRaw(string text) => File.AppendAllText(FullPath, text, new UTF8Encoding(false));

	// ── calls ────────────────────────────────────────────────────────────────────────────────

	/// <summary>Calls a method through the store-backed dispatcher and returns the whole JSON-RPC response.</summary>
	public Task<JsonNode> RawAsync(string method, string paramsJson, TdcpTransport transport = TdcpTransport.Relay) =>
		CallAsync(Dispatcher, method, paramsJson, transport);

	/// <summary>Calls a method through the store-less reference dispatcher.</summary>
	public Task<JsonNode> LegacyRawAsync(string method, string paramsJson, TdcpTransport transport = TdcpTransport.Relay) =>
		CallAsync(LegacyDispatcher, method, paramsJson, transport);

	/// <summary>Like <see cref="RawAsync"/> but returns <c>result</c> and fails on an error response.</summary>
	public async Task<JsonNode> ResultAsync(string method, string paramsJson, TdcpTransport transport = TdcpTransport.Relay) =>
		Unwrap(await RawAsync(method, paramsJson, transport));

	public async Task<JsonNode> LegacyResultAsync(string method, string paramsJson, TdcpTransport transport = TdcpTransport.Relay) =>
		Unwrap(await LegacyRawAsync(method, paramsJson, transport));

	public string NodeParams(string extra = "") =>
		$"{{\"nodeId\":\"{Session.NodeId}\"{(extra.Length == 0 ? string.Empty : "," + extra)}}}";

	public static int ErrorCode(JsonNode response) => response["error"]!["code"]!.GetValue<int>();

	public static string ErrorMessage(JsonNode response) => response["error"]!["message"]!.GetValue<string>();

	public void Dispose()
	{
		Store.Dispose();
		Epochs.Dispose();
		try
		{
			System.IO.Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	private static async Task<JsonNode> CallAsync(TdcpDispatcher dispatcher, string method, string paramsJson, TdcpTransport transport)
	{
		var request = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson}}}""";
		return (await dispatcher.HandleTextAsync(request, transport, CancellationToken.None))!;
	}

	private static JsonNode Unwrap(JsonNode response)
	{
		if (response["error"] != null)
			throw new InvalidOperationException("Unexpected error response: " + response["error"]);

		return response["result"]!;
	}

	/// <summary>The keys of <paramref name="node"/> (recursively) without <paramref name="names"/>: the pre-P1.F view of a response.</summary>
	public static JsonNode Without(JsonNode node, params string[] names)
	{
		var copy = node.DeepClone();
		Strip(copy, names);
		return copy;
	}

	private static void Strip(JsonNode? node, string[] names)
	{
		switch (node)
		{
			case JsonObject obj:
				foreach (var name in names)
					obj.Remove(name);

				foreach (var child in obj)
					Strip(child.Value, names);
				break;
			case JsonArray array:
				foreach (var item in array)
					Strip(item, names);
				break;
		}
	}

	public static string Canonical(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
}
