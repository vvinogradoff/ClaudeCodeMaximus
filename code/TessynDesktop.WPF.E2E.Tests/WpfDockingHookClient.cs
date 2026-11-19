using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// The E2E side of the docking test hook (FR.6.22, FR.29.26): connects to the pipe <c>tessyn-wpf-test-&lt;pid&gt;</c> of a <c>--test-window</c> app, sends one JSON request per line and reads
/// one JSON reply per line. A test drives docking, tabbing, floating, auto-hide, slide-out, close, show, bring-forward and reset by calls instead of mouse drags.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class WpfDockingHookClient : IDisposable
{
	private const string PipePrefix = "tessyn-wpf-test-";

	private readonly NamedPipeClientStream _pipe;
	private readonly StreamReader _reader;
	private readonly StreamWriter _writer;

	private WpfDockingHookClient(NamedPipeClientStream pipe)
	{
		_pipe = pipe;
		_reader = new StreamReader(pipe, new UTF8Encoding(false));
		_writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
	}

	/// <summary>Connects to the app's pipe (it is served once the main window is rendered); fails the test when it does not appear in time.</summary>
	public static async Task<WpfDockingHookClient> ConnectAsync(Process process, TimeSpan? timeout = null)
	{
		var pipe = new NamedPipeClientStream(".", PipePrefix + process.Id, PipeDirection.InOut, PipeOptions.Asynchronous);
		using var cts = new CancellationTokenSource(timeout ?? PreviewWindowsHarness.StepTimeout);
		try
		{
			await pipe.ConnectAsync(cts.Token);
		}
		catch (OperationCanceledException)
		{
			pipe.Dispose();
			throw new Xunit.Sdk.XunitException($"The docking test hook pipe {PipePrefix}{process.Id} did not appear.");
		}

		return new WpfDockingHookClient(pipe);
	}

	/// <summary>True when no pipe of the process exists (the pipe gate).</summary>
	public static bool PipeExists(int processId) => Directory.EnumerateFiles(@"\\.\pipe\").Any(f => f.EndsWith(PipePrefix + processId, StringComparison.Ordinal));

	public Task<DockingHookReply> StateAsync() => SendAsync(new JsonObject { ["op"] = "state" });

	public Task<DockingHookReply> DockAsync(string panel, string side) => SendAsync(Op("dock", panel, side));

	public Task<DockingHookReply> TabAsync(string panel, string withPanel) => SendAsync(Op("tab", panel, withPanel));

	public Task<DockingHookReply> FloatAsync(string panel, DockingHookRect? rect = null)
	{
		var request = Op("float", panel, null);
		if (rect != null)
		{
			request["x"] = rect.Left;
			request["y"] = rect.Top;
			request["w"] = rect.Width;
			request["h"] = rect.Height;
		}

		return SendAsync(request);
	}

	public Task<DockingHookReply> AutoHideAsync(string panel, bool on) => SendAsync(Op("autohide", panel, on ? "on" : "off"));

	public Task<DockingHookReply> SlideOutAsync(string panel) => SendAsync(Op("slideout", panel, null));

	public Task<DockingHookReply> CloseAsync(string panel) => SendAsync(Op("close", panel, null));

	public Task<DockingHookReply> ShowAsync(string panel) => SendAsync(Op("show", panel, null));

	public Task<DockingHookReply> BringForwardAsync(string panel) => SendAsync(Op("bringforward", panel, null));

	public Task<DockingHookReply> ResetAsync() => SendAsync(new JsonObject { ["op"] = "reset" });

	/// <summary>Asks for the state until <paramref name="predicate"/> holds; fails the test with the panels' places when it does not within the step timeout.</summary>
	public async Task<DockingHookReply> WaitStateAsync(Func<DockingHookReply, bool> predicate, string what)
	{
		var deadline = DateTime.UtcNow + PreviewWindowsHarness.StepTimeout;
		DockingHookReply? last = null;
		while (DateTime.UtcNow < deadline)
		{
			last = await StateAsync();
			if (predicate(last))
				return last;

			await Task.Delay(200);
		}

		Xunit.Assert.Fail($"Expected {what}. Panels: " + string.Join(" | ", last?.Panels.Select(p => $"{p.Id}={(p.Shown ? p.Side : "closed")},pane{p.Pane},tab{p.TabIndex},sel{p.Selected},hide{p.AutoHide},slid{p.SlidOut}") ?? []));
		return last!;
	}

	/// <summary>Sends one request and parses the reply line.</summary>
	public async Task<DockingHookReply> SendAsync(JsonObject request)
	{
		await _writer.WriteLineAsync(request.ToJsonString());
		var line = await _reader.ReadLineAsync().WaitAsync(PreviewWindowsHarness.StepTimeout) ?? throw new Xunit.Sdk.XunitException("The docking test hook closed the pipe.");
		return Parse(line);
	}

	/// <summary>Sends text that is not a request, as it is (for the error replies).</summary>
	public async Task<DockingHookReply> SendRawAsync(string line)
	{
		await _writer.WriteLineAsync(line);
		var reply = await _reader.ReadLineAsync().WaitAsync(PreviewWindowsHarness.StepTimeout) ?? throw new Xunit.Sdk.XunitException("The docking test hook closed the pipe.");
		return Parse(reply);
	}

	public void Dispose()
	{
		_writer.Dispose();
		_reader.Dispose();
		_pipe.Dispose();
	}

	private static JsonObject Op(string op, string panel, string? arg)
	{
		var request = new JsonObject { ["op"] = op, ["panel"] = panel };
		if (arg != null)
			request["arg"] = arg;

		return request;
	}

	private static DockingHookReply Parse(string line)
	{
		var node = JsonNode.Parse(line)!.AsObject();
		var panels = (node["panels"]?.AsArray() ?? []).Select(p => p!.AsObject()).Select(p => new DockingHookPanel(
			p["id"]!.GetValue<string>(), p["kind"]!.GetValue<string>(), p["shown"]!.GetValue<bool>(), p["side"]!.GetValue<string>(), p["pane"]!.GetValue<int>(), p["tabIndex"]!.GetValue<int>(),
			p["selected"]!.GetValue<bool>(), p["width"]!.GetValue<double>(), p["height"]!.GetValue<double>(), p["autoHide"]!.GetValue<bool>(), p["edge"]?.GetValue<string>(), p["floating"]!.GetValue<bool>(),
			p["floatRect"] is JsonObject r ? new DockingHookRect(r["left"]!.GetValue<double>(), r["top"]!.GetValue<double>(), r["width"]!.GetValue<double>(), r["height"]!.GetValue<double>()) : null,
			p["badge"]?.GetValue<string>(), p["slidOut"]?.GetValue<bool>() ?? false)).ToList();
		return new DockingHookReply(node["ok"]!.GetValue<bool>(), node["error"]?.GetValue<string>(), panels);
	}
}
