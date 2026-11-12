using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

internal enum SampleKind { FirstKind, SecondKind }

internal sealed record SampleResult(string Name, string? Missing, SampleKind Kind);

/// <summary>Registers the methods the dispatcher tests call.</summary>
internal sealed class SampleRpcModule : IRemoteRpcModule
{
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync("sample.local", _ => new SampleResult("local", null, SampleKind.SecondKind), remoteCallable: false);
		registry.AddSync("sample.remote", _ => new SampleResult("remote", null, SampleKind.FirstKind), remoteCallable: true);
		registry.AddSync("sample.null", _ => null, remoteCallable: true);
		registry.AddSync("sample.echo", p => new SampleResult(TdcpParams.RequireString(p, "name"), null, SampleKind.FirstKind), remoteCallable: true);
		registry.AddSync("sample.number", p => TdcpParams.OptionalInt(p, "n") ?? 0, remoteCallable: true);
		registry.AddSync("sample.daemonError", _ => throw new TdcpException("Session not found"), remoteCallable: true);
		registry.AddSync("sample.format", _ => throw new FormatException("bad value"), remoteCallable: true);
		registry.AddSync("sample.boom", _ => throw new InvalidOperationException("secret internal detail"), remoteCallable: true);
		registry.Add("sample.async", async (_, ct) =>
		{
			await Task.Yield();
			return new SampleResult("async", null, SampleKind.FirstKind);
		}, remoteCallable: true);
		registry.Add("sample.cancel", (_, ct) => Task.FromCanceled<object?>(new CancellationToken(canceled: true)), remoteCallable: true);
	}
}

/// <remarks>Created by Claude</remarks>
public sealed class TdcpDispatcherTests
{
	private static TdcpDispatcher Create(params IRemoteRpcModule[] modules) =>
		new(modules.Length == 0 ? [new SampleRpcModule()] : modules);

	private static Task<JsonNode> Send(TdcpDispatcher dispatcher, string json, TdcpTransport transport = TdcpTransport.Loopback) =>
		dispatcher.HandleTextAsync(json, transport, CancellationToken.None);

	private static int ErrorCode(JsonNode response) => response["error"]!["code"]!.GetValue<int>();

	private static string ErrorMessage(JsonNode response) => response["error"]!["message"]!.GetValue<string>();

	[Fact]
	public async Task Success_ReturnsResultWithRequestId_AndSerializesCamelCaseWithoutNulls()
	{
		var response = await Send(Create(), """{"jsonrpc":"2.0","id":7,"method":"sample.local"}""");

		Assert.Equal("2.0", response["jsonrpc"]!.GetValue<string>());
		Assert.Equal(7, response["id"]!.GetValue<int>());
		Assert.Null(response["error"]);
		var result = response["result"]!;
		Assert.Equal("local", result["name"]!.GetValue<string>());
		Assert.Equal("secondKind", result["kind"]!.GetValue<string>());
		Assert.False(((JsonObject)result).ContainsKey("missing"));
	}

	[Fact]
	public async Task StringId_IsEchoed()
	{
		var response = await Send(Create(), """{"jsonrpc":"2.0","id":"r-17","method":"sample.local"}""");

		Assert.Equal("r-17", response["id"]!.GetValue<string>());
	}

	[Fact]
	public async Task NullResult_YieldsNullResultMember()
	{
		var response = await Send(Create(), """{"jsonrpc":"2.0","id":1,"method":"sample.null"}""");

		Assert.True(((JsonObject)response).ContainsKey("result"));
		Assert.Null(response["result"]);
		Assert.Null(response["error"]);
	}

	[Fact]
	public async Task AsyncHandler_IsAwaited()
	{
		var response = await Send(Create(), """{"id":1,"method":"sample.async"}""");

		Assert.Equal("async", response["result"]!["name"]!.GetValue<string>());
	}

	[Fact]
	public async Task UnparseableText_ReturnsParseError()
	{
		var response = await Send(Create(), "{ not json");

		Assert.Equal(-32700, ErrorCode(response));
		Assert.Null(response["id"]);
	}

	[Theory]
	[InlineData("[1,2]")]
	[InlineData("42")]
	[InlineData("null")]
	[InlineData("""{"id":1}""")]
	[InlineData("""{"id":1,"method":""}""")]
	[InlineData("""{"id":1,"method":5}""")]
	[InlineData("""{"id":{"a":1},"method":"sample.local"}""")]
	public async Task MalformedRequest_ReturnsInvalidRequest(string json)
	{
		var response = await Send(Create(), json);

		Assert.Equal(-32600, ErrorCode(response));
	}

	[Fact]
	public async Task NonObjectParams_ReturnInvalidParams_WithRequestId()
	{
		var response = await Send(Create(), """{"id":3,"method":"sample.echo","params":[1]}""");

		Assert.Equal(-32602, ErrorCode(response));
		Assert.Equal(3, response["id"]!.GetValue<int>());
	}

	[Fact]
	public async Task UnknownMethod_ReturnsMethodNotFound()
	{
		var response = await Send(Create(), """{"id":1,"method":"nope.nothing"}""");

		Assert.Equal(-32601, ErrorCode(response));
		Assert.Contains("nope.nothing", ErrorMessage(response));
	}

	[Fact]
	public async Task Relay_RefusesMethodsThatAreNotRemoteCallable_ButLoopbackAllowsThem()
	{
		var dispatcher = Create();

		var relay = await Send(dispatcher, """{"id":1,"method":"sample.local"}""", TdcpTransport.Relay);
		var loopback = await Send(dispatcher, """{"id":1,"method":"sample.local"}""", TdcpTransport.Loopback);

		Assert.Equal(-32601, ErrorCode(relay));
		Assert.Null(loopback["error"]);
	}

	[Fact]
	public async Task Relay_AllowsRemoteCallableMethods()
	{
		var response = await Send(Create(), """{"id":1,"method":"sample.remote"}""", TdcpTransport.Relay);

		Assert.Equal("remote", response["result"]!["name"]!.GetValue<string>());
	}

	[Fact]
	public async Task MissingRequiredParam_ReturnsInvalidParams()
	{
		var response = await Send(Create(), """{"id":1,"method":"sample.echo","params":{}}""");

		Assert.Equal(-32602, ErrorCode(response));
		Assert.Contains("name", ErrorMessage(response));
	}

	[Fact]
	public async Task WronglyTypedParam_ReturnsInvalidParams()
	{
		var response = await Send(Create(), """{"id":1,"method":"sample.echo","params":{"name":12}}""");

		Assert.Equal(-32602, ErrorCode(response));
	}

	[Fact]
	public async Task NonIntegerParam_ReturnsInvalidParams()
	{
		var response = await Send(Create(), """{"id":1,"method":"sample.number","params":{"n":"x"}}""");

		Assert.Equal(-32602, ErrorCode(response));
	}

	[Fact]
	public async Task FormatException_ReturnsInvalidParams()
	{
		var response = await Send(Create(), """{"id":1,"method":"sample.format"}""");

		Assert.Equal(-32602, ErrorCode(response));
	}

	[Fact]
	public async Task TdcpException_ReturnsDaemonErrorWithItsMessage()
	{
		var response = await Send(Create(), """{"id":1,"method":"sample.daemonError"}""");

		Assert.Equal(-32000, ErrorCode(response));
		Assert.Equal("Session not found", ErrorMessage(response));
	}

	[Fact]
	public async Task AnyOtherException_ReturnsInternalError_WithoutLeakingTheMessage()
	{
		var response = await Send(Create(), """{"id":9,"method":"sample.boom"}""");

		Assert.Equal(-32603, ErrorCode(response));
		Assert.DoesNotContain("secret", ErrorMessage(response));
		Assert.Equal(9, response["id"]!.GetValue<int>());
	}

	[Fact]
	public async Task CancelledHandler_ReturnsInternalError_InsteadOfThrowing()
	{
		var response = await Send(Create(), """{"id":1,"method":"sample.cancel"}""");

		Assert.Equal(-32603, ErrorCode(response));
	}

	[Fact]
	public async Task HandleRequestAsync_AcceptsAParsedNode()
	{
		var request = JsonNode.Parse("""{"id":1,"method":"sample.remote"}""")!;

		var response = await Create().HandleRequestAsync(request, TdcpTransport.Relay, CancellationToken.None);

		Assert.Equal("remote", response["result"]!["name"]!.GetValue<string>());
	}

	[Fact]
	public void DuplicateRegistration_IsLoggedAndDoesNotThrowAtConstruction()
	{
		var dispatcher = new TdcpDispatcher([new SampleRpcModule(), new SampleRpcModule()]);

		Assert.True(dispatcher.IsRemoteCallable("sample.remote"));
	}

	[Fact]
	public void Registry_RejectsBlankAndDuplicateNames()
	{
		var registry = new TdcpMethodRegistry();
		registry.AddSync("a.b", _ => null, remoteCallable: false);

		Assert.Throws<InvalidOperationException>(() => registry.AddSync("a.b", _ => null, remoteCallable: false));
		Assert.Throws<InvalidOperationException>(() => registry.AddSync(" ", _ => null, remoteCallable: false));
	}

	[Fact]
	public void RemoteMethodNames_ListsOnlyRemoteCallableMethods()
	{
		var names = Create().RemoteMethodNames;

		Assert.Contains("sample.remote", names);
		Assert.DoesNotContain("sample.local", names);
	}

	// ── client.hello ─────────────────────────────────────────────────────────────

	[Fact]
	public async Task ClientHello_IsRemoteCallable_AndReportsProtocolAndRunId()
	{
		var dispatcher = new TdcpDispatcher([new ClientRpcModule()]);

		var first = await Send(dispatcher, """{"id":1,"method":"client.hello"}""", TdcpTransport.Relay);
		var second = await Send(dispatcher, """{"id":2,"method":"client.hello"}""", TdcpTransport.Relay);

		var result = first["result"]!;
		Assert.Equal(Constants.Remote.ProtocolVersion, result["protocolVersion"]!.GetValue<int>());
		Assert.Equal(Environment.MachineName, result["machineName"]!.GetValue<string>());
		Assert.Matches("^[0-9a-f]{32}$", result["runId"]!.GetValue<string>());
		Assert.NotNull(result["daemonVersion"]);
		Assert.Equal(result["runId"]!.GetValue<string>(), second["result"]!["runId"]!.GetValue<string>());
		Assert.True(DateTimeOffset.TryParse(result["serverTimeUtc"]!.GetValue<string>(), out var time));
		Assert.InRange((DateTimeOffset.UtcNow - time).Duration().TotalSeconds, 0, 60);
	}

	[Fact]
	public async Task ClientHello_RunIdDiffersBetweenModuleInstances()
	{
		var a = await Send(new TdcpDispatcher([new ClientRpcModule()]), """{"id":1,"method":"client.hello"}""");
		var b = await Send(new TdcpDispatcher([new ClientRpcModule()]), """{"id":1,"method":"client.hello"}""");

		Assert.NotEqual(a["result"]!["runId"]!.GetValue<string>(), b["result"]!["runId"]!.GetValue<string>());
	}

	[Fact]
	public async Task ClientHello_UsesTheInjectedClock()
	{
		var time = new ManualTimeProvider();
		var dispatcher = new TdcpDispatcher([new ClientRpcModule(time)]);

		var response = await Send(dispatcher, """{"id":1,"method":"client.hello"}""");

		var reported = DateTimeOffset.Parse(response["result"]!["serverTimeUtc"]!.GetValue<string>());
		Assert.Equal(time.GetUtcNow(), reported);
	}

	// ── Existing TDCP methods stay loopback-only ───────────────────────────────────

	[Theory]
	[InlineData("tree.list")]
	[InlineData("tree.getSelected")]
	[InlineData("tree.setSelected")]
	[InlineData("tree.findByNodeId")]
	[InlineData("tree.findByToken")]
	[InlineData("tree.location")]
	[InlineData("turn.isRunning")]
	[InlineData("turn.setRunning")]
	[InlineData("turn.run")]
	[InlineData("turn.cancel")]
	[InlineData("turn.resolveProfile")]
	[InlineData("schedule.list")]
	[InlineData("schedule.add")]
	[InlineData("schedule.remove")]
	[InlineData("content.read")]
	[InlineData("content.exists")]
	[InlineData("content.path")]
	public async Task LegacyTdcpMethods_AreRegistered_ButNotRemoteCallable(string method)
	{
		var dispatcher = new TdcpDispatcher([new TdcpLoopbackRpcModule(new WireFakeDaemonClient())]);

		Assert.False(dispatcher.IsRemoteCallable(method));
		var relay = await Send(dispatcher, "{\"id\":1,\"method\":\"" + method + "\",\"params\":{}}", TdcpTransport.Relay);
		Assert.Equal(-32601, ErrorCode(relay));

		// Over the loopback the method exists: a missing param is "invalid params", never "method not found".
		var loopback = await Send(dispatcher, "{\"id\":1,\"method\":\"" + method + "\",\"params\":{}}", TdcpTransport.Loopback);
		var loopbackCode = loopback["error"] == null ? 0 : ErrorCode(loopback);
		Assert.NotEqual(-32601, loopbackCode);
	}

	[Fact]
	public async Task LegacyTurnRun_WithoutNodeId_IsInvalidParamsOverLoopback()
	{
		var dispatcher = new TdcpDispatcher([new TdcpLoopbackRpcModule(new WireFakeDaemonClient())]);

		var response = await Send(dispatcher, """{"id":1,"method":"turn.run","params":{"prompt":"x"}}""");

		Assert.Equal(-32602, ErrorCode(response));
	}
}
