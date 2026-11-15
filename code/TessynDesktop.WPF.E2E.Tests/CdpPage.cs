using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// Drives the transcript page of the BUILT app through the Chrome DevTools protocol: the app is started with the WebView2 remote debugging port
/// (<see cref="EnvironmentFor"/>), and this class sends real mouse and key input to the page and reads its state. The input goes to the page itself, so
/// the test never needs the window to be in the foreground and nothing is typed into whatever else the machine is showing.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CdpPage : IDisposable
{
	private readonly ClientWebSocket _socket;
	private int _id;

	private CdpPage(ClientWebSocket socket) => _socket = socket;

	/// <summary>A free loopback TCP port for the debugging endpoint.</summary>
	public static int FreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	/// <summary>The environment variable that makes WebView2 listen for DevTools connections on <paramref name="port"/>.</summary>
	public static Dictionary<string, string> EnvironmentFor(int port) =>
		new() { ["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = $"--remote-debugging-port={port}" };

	/// <summary>Connects to the page whose URL contains <paramref name="urlPart"/>; waits for it to exist.</summary>
	public static async Task<CdpPage> ConnectAsync(int port, string urlPart, TimeSpan timeout)
	{
		using var http = new HttpClient();
		var deadline = DateTime.UtcNow + timeout;
		string? lastError = null;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				var targets = JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json"))!.AsArray();
				var target = targets.FirstOrDefault(t => t?["type"]?.GetValue<string>() == "page" && (t["url"]?.GetValue<string>() ?? string.Empty).Contains(urlPart, StringComparison.OrdinalIgnoreCase));
				if (target?["webSocketDebuggerUrl"]?.GetValue<string>() is { } wsUrl)
				{
					var socket = new ClientWebSocket();
					await socket.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
					return new CdpPage(socket);
				}

				lastError = "no page target with " + urlPart + ": " + string.Join(", ", targets.Select(t => t?["url"]?.GetValue<string>()));
			}
			catch (Exception ex) when (ex is HttpRequestException or WebSocketException or TaskCanceledException)
			{
				lastError = ex.Message;
			}

			await Task.Delay(250);
		}

		throw new TimeoutException("Could not reach the transcript page over the DevTools port: " + lastError + (IsElevated() ? " The test process is ELEVATED, and WebView2 ignores WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS (so the debugging port is never opened) when its host runs elevated. Run through toolse2eRun-WpfE2E.ps1 in its test-user mode, or from a non-elevated shell." : ""));
	}

	/// <summary>Whether this process runs elevated (administrator token): WebView2 then drops the additional browser arguments, including the debugging port.</summary>
	private static bool IsElevated()
	{
		using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
		return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
	}

	public void Dispose() => _socket.Dispose();

	public async Task<JsonNode?> SendAsync(string method, JsonObject? parameters = null)
	{
		var id = ++_id;
		var request = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() };
		await _socket.SendAsync(Encoding.UTF8.GetBytes(request.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);

		var buffer = new byte[64 * 1024];
		while (true)
		{
			using var message = new MemoryStream();
			WebSocketReceiveResult received;
			do
			{
				received = await _socket.ReceiveAsync(buffer, CancellationToken.None);
				message.Write(buffer, 0, received.Count);
			}
			while (!received.EndOfMessage);

			var node = JsonNode.Parse(Encoding.UTF8.GetString(message.ToArray()))!;
			if (node["id"]?.GetValue<int>() != id)
				continue;   // an event, or a late answer to something else

			if (node["error"] != null)
				throw new InvalidOperationException($"{method} failed: {node["error"]}");

			return node["result"];
		}
	}

	/// <summary>Evaluates <paramref name="expression"/> in the page and returns its value as JSON (the expression must produce plain data).</summary>
	public async Task<JsonNode?> EvalAsync(string expression)
	{
		var result = await SendAsync("Runtime.evaluate", new JsonObject { ["expression"] = expression, ["returnByValue"] = true, ["awaitPromise"] = true });
		if (result?["exceptionDetails"] != null)
			throw new InvalidOperationException("Script failed: " + result["exceptionDetails"] + " in: " + expression);

		return result?["result"]?["value"];
	}

	/// <summary>
	/// Waits for a button of the page whose text is exactly <paramref name="text"/>, scrolls it into view and clicks it with a real mouse click; false on timeout.
	/// The page's content cannot be reached through UI Automation in a non-interactive session (UIA hides the invisible child windows of the browser), so buttons inside the page are found here.
	/// </summary>
	public async Task<bool> ClickButtonAsync(string text, TimeSpan timeout)
	{
		var script = "((t) => { const b = Array.from(document.querySelectorAll('button')).find(x => x.textContent.trim() === t); if (!b) return null; b.scrollIntoView({ block: 'center' }); const r = b.getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })(" + System.Text.Json.JsonSerializer.Serialize(text) + ")";
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			if (await EvalAsync(script) is JsonArray point)
			{
				await Task.Delay(100);   // the scroll above is applied before the position is read again
				if (await EvalAsync(script) is JsonArray settled)
				{
					await ClickAsync(settled[0]!.GetValue<double>(), settled[1]!.GetValue<double>());
					return true;
				}
			}

			await Task.Delay(250);
		}

		return false;
	}

	/// <summary>A left click at page coordinates (CSS pixels).</summary>
	public async Task ClickAsync(double x, double y)
	{
		await MouseAsync("mouseMoved", x, y, 0);
		await MouseAsync("mousePressed", x, y, 1);
		await MouseAsync("mouseReleased", x, y, 1);
	}

	/// <summary>A drag from one point to another with the left button held (selects text under it).</summary>
	public async Task DragAsync(double fromX, double fromY, double toX, double toY)
	{
		await MouseAsync("mouseMoved", fromX, fromY, 0);
		await MouseAsync("mousePressed", fromX, fromY, 1);
		await MouseAsync("mouseMoved", (fromX + toX) / 2, (fromY + toY) / 2, 0, buttons: 1);
		await MouseAsync("mouseMoved", toX, toY, 0, buttons: 1);
		await MouseAsync("mouseReleased", toX, toY, 1);
	}

	private Task MouseAsync(string type, double x, double y, int clickCount, int buttons = 0) =>
		SendAsync("Input.dispatchMouseEvent", new JsonObject
		{
			["type"] = type, ["x"] = x, ["y"] = y, ["button"] = type == "mouseMoved" && buttons == 0 ? "none" : "left", ["buttons"] = type == "mousePressed" ? 1 : buttons, ["clickCount"] = clickCount,
		});

	/// <summary>A key press (down and up) with the Windows virtual key code the browser reports for it.</summary>
	/// <param name="modifiers">DevTools modifier bits: 1 Alt, 2 Ctrl, 4 Meta, 8 Shift.</param>
	public async Task PressAsync(string key, int virtualKey, int modifiers = 0)
	{
		foreach (var type in new[] { "rawKeyDown", "keyUp" })
			await SendAsync("Input.dispatchKeyEvent", new JsonObject { ["type"] = type, ["key"] = key, ["code"] = key, ["windowsVirtualKeyCode"] = virtualKey, ["nativeVirtualKeyCode"] = virtualKey, ["modifiers"] = modifiers });
	}
}
