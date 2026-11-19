using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary><c>client.hello</c> with capabilities, min protocol version and the event seq (P1.C).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class HelloTests : IDisposable
{
	private readonly RemoteEventBus _bus = new();
	private readonly EventLog _log;
	private readonly DaemonRunInfo _run = new();
	private readonly TdcpDispatcher _dispatcher;

	public HelloTests()
	{
		_log = new EventLog(_bus);
		_dispatcher = new TdcpDispatcher([new ClientRpcModule(runInfo: _run, eventLog: _log)]);
	}

	public void Dispose()
	{
		_log.Dispose();
		_bus.Dispose();
	}

	private async Task<JsonNode> HelloAsync(string? paramsJson = null, TdcpTransport transport = TdcpTransport.Loopback)
	{
		var body = paramsJson == null
			? """{"jsonrpc":"2.0","id":1,"method":"client.hello"}"""
			: $$"""{"jsonrpc":"2.0","id":1,"method":"client.hello","params":{{paramsJson}}}""";
		return (await _dispatcher.HandleTextAsync(body, transport, default))!;
	}

	[Fact]
	public async Task Without_params_it_keeps_the_old_fields_and_adds_the_new_ones()
	{
		var result = (await HelloAsync())["result"]!;

		Assert.Equal(1, result["protocolVersion"]!.GetValue<int>());
		Assert.Equal(_run.RunId, result["runId"]!.GetValue<string>());
		Assert.NotNull(result["daemonVersion"]);
		Assert.NotNull(result["machineName"]);
		Assert.NotNull(result["serverTimeUtc"]);
		Assert.Equal(Constants.Remote.MinProtocolVersion, result["minProtocolVersion"]!.GetValue<int>());
		Assert.Equal(0, result["eventSeq"]!.GetValue<long>());
		Assert.Equal(
			[Constants.Remote.Capabilities.EventsLocal, Constants.Remote.Capabilities.EventsResume],
			result["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray());
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Elevated_says_whether_the_daemon_process_runs_elevated(bool elevated)
	{
		// FR.32.12, tdcp-spec 10.A10.
		var dispatcher = new TdcpDispatcher([new ClientRpcModule(runInfo: _run, eventLog: _log, elevated: elevated)]);

		var reply = await dispatcher.HandleTextAsync("""{"jsonrpc":"2.0","id":1,"method":"client.hello"}""", TdcpTransport.Loopback, default);

		Assert.Equal(elevated, reply!["result"]!["elevated"]!.GetValue<bool>());
	}

	[Fact]
	public async Task Without_an_override_elevated_is_the_process_s_own_elevation_and_the_relay_gets_it_too()
	{
		var loopback = (await HelloAsync())["result"]!;
		var relay = (await HelloAsync(transport: TdcpTransport.Relay))["result"]!;

		Assert.Equal(Environment.IsPrivilegedProcess, loopback["elevated"]!.GetValue<bool>());
		Assert.Equal(Environment.IsPrivilegedProcess, relay["elevated"]!.GetValue<bool>());
	}

	[Fact]
	public async Task EventSeq_is_the_current_head_of_the_event_log()
	{
		_bus.Publish("session.changed", new { nodeId = "a" });
		_bus.Publish("session.changed", new { nodeId = "b" });

		var result = (await HelloAsync())["result"]!;

		Assert.Equal(2, result["eventSeq"]!.GetValue<long>());
	}

	[Fact]
	public async Task Client_identity_params_are_accepted_over_both_transports()
	{
		const string identity = """{"clientName":"TessynWpf","clientVersion":"0.1.0","protocolVersion":1,"capabilities":["events.resume"]}""";

		Assert.NotNull((await HelloAsync(identity))["result"]);
		Assert.NotNull((await HelloAsync(identity, TdcpTransport.Relay))["result"]);
	}

	[Theory]
	[InlineData("""{"clientName":5}""")]
	[InlineData("""{"protocolVersion":"one"}""")]
	[InlineData("""{"capabilities":"events.resume"}""")]
	[InlineData("""{"capabilities":[1,2]}""")]
	[InlineData("""{"protocolVersion":0}""")]
	public async Task Malformed_hello_params_are_invalid_params(string paramsJson)
	{
		var response = await HelloAsync(paramsJson);

		Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
	}

	[Fact]
	public async Task Without_an_event_log_no_event_capabilities_are_announced()
	{
		var dispatcher = new TdcpDispatcher([new ClientRpcModule()]);

		var response = await dispatcher.HandleTextAsync("""{"jsonrpc":"2.0","id":1,"method":"client.hello"}""", TdcpTransport.Loopback, default);

		Assert.Empty(response!["result"]!["capabilities"]!.AsArray());
		Assert.Equal(0, response["result"]!["eventSeq"]!.GetValue<long>());
	}
}
