using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// The J1 methods over a real loopback <see cref="TdcpServer"/>: the real registry, settings, turn engine and the three J1 modules on top of
/// <see cref="TurnWireEnv"/> (which owns the temp dir, the fake claude processes and the first directory "proj" with the session "Wire session").
/// Only the git-origin cache is a fake, so the move rule can be driven. Disposing disposes both hosts and deletes the temp dir.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TreeMutationWireEnv : IDisposable
{
	public TurnWireEnv Turn { get; } = new();
	public FakeDirectoryGitOrigins Origins { get; } = new();
	public TdcpDispatcher Dispatcher { get; }
	public TdcpTestHost Host { get; }

	public TreeMutationWireEnv(bool withServices = true)
	{
		var labels   = new FakeDirectoryLabelService();
		var tree     = new TreeMutationService(Turn.Registry, Turn.Settings, labels, Origins);
		var resolver = new TurnSettingsResolver(Turn.Settings, Turn.Turns, Turn.ProfileService, Turn.Models, new ProfileLabelService(Turn.ProfileService, Turn.Settings));
		var options  = new SessionOptionsService(Turn.Registry, Turn.Settings, Turn.Engine, resolver, TestEngines.Claude(Turn.Streaming), Turn.Turns);
		var local    = new LocalToolsService(Turn.Registry, Turn.Settings);

		var runInfo = new DaemonRunInfo();
		var log     = new EventLog(Turn.Bus);
		Dispatcher = new TdcpDispatcher(
		[
			withServices
				? new ClientRpcModule(runInfo: runInfo, eventLog: log, treeMutation: tree, sessionOptions: options, localTools: local)
				: new ClientRpcModule(runInfo: runInfo, eventLog: log),
			new TreeMutationRpcModule(tree),
			new SessionOptionsRpcModule(options),
			new LocalToolsRpcModule(local),
			new TurnRpcModule(Turn.Registry, new TurnOptionsResolver(Turn.Models, Turn.Settings, new ProfileLabelService(Turn.ProfileService, Turn.Settings)), resolver, new TurnSendService(Turn.Engine, Turn.Registry), Turn.Engine, Turn.Turns, TestEngines.Claude(Turn.Streaming), new SessionModelSettings(Turn.Settings)),
		]);
		Host = new TdcpTestHost(Dispatcher, bus: Turn.Bus, runInfo: runInfo);
	}

	public string Root => Path.GetDirectoryName(Turn.Registry.Directories[0].Path)!;

	/// <summary>The directory "proj" and the session "Wire session" the underlying environment created.</summary>
	public DirectoryNodeModel Directory => Turn.Registry.Directories[0];

	public SessionNodeModel Session => Turn.Session;

	/// <summary>Creates a folder under the root and adds it as a directory.</summary>
	public DirectoryNodeModel AddDirectory(string name)
	{
		var path = Path.Combine(Root, name);
		System.IO.Directory.CreateDirectory(path);
		return Turn.Registry.AddDirectory(path);
	}

	/// <summary>Calls a method over HTTP and returns the whole JSON-RPC response.</summary>
	public Task<JsonNode> CallAsync(string method, JsonNode? @params = null) => Host.CallAsync(method, @params);

	/// <summary>The same call through the dispatcher as the relay would make it (only remote-callable methods exist there).</summary>
	public async Task<JsonNode> CallAsRelayAsync(string method, JsonNode? @params = null)
	{
		var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = @params?.DeepClone() ?? new JsonObject() };
		return (await Dispatcher.HandleTextAsync(request.ToJsonString(), TdcpTransport.Relay, CancellationToken.None))!;
	}

	public void Dispose()
	{
		Host.Dispose();
		Turn.Dispose();
	}
}
