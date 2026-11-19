using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.2.14 in a real browser (TC.MT.03): the real tables of this repo's docs are rendered by <see cref="MarkdigEntryRenderer"/>, put into the CSS of the preview
/// page and of the transcript page, and laid out by headless Edge. Every cell must be at least as wide as its longest word, and a table that is too wide must
/// scroll in its own box while the entry and the page do not. Reported as skipped when Edge is not installed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TableLayoutBrowserTests : IDisposable
{
	private const string MeasureScript = """
		<script>
		function words(td){var out=[];var w=document.createTreeWalker(td,NodeFilter.SHOW_TEXT);var n;while(n=w.nextNode()){var cs=getComputedStyle(n.parentElement);
		n.textContent.split(/\s+/).filter(Boolean).forEach(function(x){var s=document.createElement('span');s.style.cssText='position:absolute;visibility:hidden;white-space:nowrap;font:'+cs.font;s.textContent=x;document.body.appendChild(s);out.push({t:x,w:s.getBoundingClientRect().width});s.remove();});}return out;}
		var bad=[],tables=[],host=document.querySelector('#host');
		document.querySelectorAll('table').forEach(function(t){var scrolls=t.scrollWidth>t.clientWidth+1,cells=0;
		t.querySelectorAll('th,td').forEach(function(td){cells++;var cs=getComputedStyle(td),inner=td.getBoundingClientRect().width-parseFloat(cs.paddingLeft)-parseFloat(cs.paddingRight)-parseFloat(cs.borderLeftWidth)-parseFloat(cs.borderRightWidth);
		var longest=words(td).sort(function(a,b){return b.w-a.w;})[0];if(longest&&inner<longest.w-1.5)bad.push(longest.t+' '+inner.toFixed(1)+'<'+longest.w.toFixed(1));});
		tables.push({cells:cells,scrolls:scrolls,boxWidth:t.clientWidth});});
		document.body.insertAdjacentHTML('beforeend','<pre id="result">'+JSON.stringify({bad:bad,tables:tables,hostOverflow:host.scrollWidth>host.clientWidth+1,pageOverflow:document.documentElement.scrollWidth>innerWidth+1})+'</pre>');
		</script>
		""";

	private readonly string _dir = Path.Combine(Path.GetTempPath(), "tessyn-table-layout-" + Guid.NewGuid().ToString("N"));

	public TableLayoutBrowserTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try
		{
			Directory.Delete(_dir, recursive: true);
		}
		catch (IOException)
		{
			// Edge may still hold the profile folder for a moment; it is in %TEMP%.
		}
	}

	private static string RepoFile(string relative)
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
		{
			var candidate = Path.Combine(dir.FullName, relative);
			if (File.Exists(candidate))
				return candidate;
		}

		throw new FileNotFoundException(relative);
	}

	private static string Web(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Web", name));

	private static string Html(string markdownFile) => new MarkdigEntryRenderer(new UrlPolicy()).RenderMarkdown(File.ReadAllText(RepoFile(markdownFile)));

	/// <summary>The page's CSS and body, with the measuring script; the host element holds the rendered markdown.</summary>
	private static string PreviewPage(string rendered)
	{
		var page = Regex.Replace(Web("preview.html"), "<meta http-equiv=\"Content-Security-Policy\"[^>]*>", string.Empty);
		return page.Replace("<div id=\"root\"></div>", "<div id=\"root\"><div id=\"host\" class=\"md\">" + rendered + "</div></div>").Replace("<script src=\"preview.js\"></script>", MeasureScript);
	}

	private static string TranscriptPage(string rendered)
	{
		var page = Regex.Replace(Web("transcript.html"), "<meta http-equiv=\"Content-Security-Policy\"[^>]*>", string.Empty);
		page = Regex.Replace(page, "<script src=\"[^\"]*\"></script>\\s*", string.Empty);
		return page.Replace("<div id=\"items\"></div>", "<div id=\"items\"><div class=\"e A\"><div class=\"c\"><div class=\"b\" id=\"host\">" + rendered + "</div></div></div></div>").Replace("</body>", MeasureScript + "</body>");
	}

	private TableReport? Measure(string html, int width)
	{
		var edge = new EdgeBrowser().Find();
		if (edge == null)
			return null;   // no Edge on this machine: nothing to measure

		var file = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".html");
		File.WriteAllText(file, html);
		var port = FreePort();
		var start = new ProcessStartInfo(edge!, $"--headless=new --disable-gpu --user-data-dir=\"{Path.Combine(_dir, "profile" + port)}\" --window-size={width},900 --remote-debugging-port={port} about:blank")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		using var process = Process.Start(start)!;
		try
		{
			var json = MeasureOverDevToolsAsync(port, new Uri(file).AbsoluteUri, width).GetAwaiter().GetResult();
			return JsonSerializer.Deserialize<TableReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
		}
		finally
		{
			process.Kill(entireProcessTree: true);
		}
	}

	private static int FreePort()
	{
		var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
		listener.Start();
		var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	/// <summary>Opens the page in the headless browser over the DevTools port (the browser is a GUI program, so it has no stdout to dump the DOM to) and returns the measurement the page wrote.</summary>
	private static async Task<string> MeasureOverDevToolsAsync(int port, string url, int width)
	{
		using var http = new System.Net.Http.HttpClient();
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
		string? wsUrl = null;
		while (wsUrl == null && DateTime.UtcNow < deadline)
		{
			try
			{
				var targets = System.Text.Json.Nodes.JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json"))!.AsArray();
				wsUrl = targets.FirstOrDefault(x => x?["type"]?.GetValue<string>() == "page")?["webSocketDebuggerUrl"]?.GetValue<string>();
			}
			catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
			{
				// the browser is still starting
			}

			if (wsUrl == null)
				await Task.Delay(200);
		}

		Assert.NotNull(wsUrl);
		using var socket = new System.Net.WebSockets.ClientWebSocket();
		await socket.ConnectAsync(new Uri(wsUrl!), CancellationToken.None);
		var id = 0;
		async Task<System.Text.Json.Nodes.JsonNode?> Send(string method, System.Text.Json.Nodes.JsonObject? args = null)
		{
			var mine = ++id;
			var request = new System.Text.Json.Nodes.JsonObject { ["id"] = mine, ["method"] = method, ["params"] = args ?? new System.Text.Json.Nodes.JsonObject() };
			await socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(request.ToJsonString()), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
			var buffer = new byte[64 * 1024];
			while (true)
			{
				using var message = new MemoryStream();
				System.Net.WebSockets.WebSocketReceiveResult received;
				do
				{
					received = await socket.ReceiveAsync(buffer, CancellationToken.None);
					message.Write(buffer, 0, received.Count);
				}
				while (!received.EndOfMessage);

				var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Encoding.UTF8.GetString(message.ToArray()))!;
				if (node["id"]?.GetValue<int>() == mine)
					return node["result"];
			}
		}

		await Send("Emulation.setDeviceMetricsOverride", new System.Text.Json.Nodes.JsonObject { ["width"] = width, ["height"] = 900, ["deviceScaleFactor"] = 1, ["mobile"] = false });
		await Send("Page.navigate", new System.Text.Json.Nodes.JsonObject { ["url"] = url });
		for (var attempt = 0; attempt < 100; attempt++)
		{
			var result = await Send("Runtime.evaluate", new System.Text.Json.Nodes.JsonObject { ["expression"] = "(document.getElementById('result')||{}).textContent||''", ["returnByValue"] = true });
			var value = result?["result"]?["value"]?.GetValue<string>();
			if (!string.IsNullOrEmpty(value))
				return value;

			await Task.Delay(100);
		}

		throw new TimeoutException("the page produced no measurement");
	}
	[EdgeFact]
	public void TheTcPwTable_ScrollsInItsOwnBox_WhenTheViewIsTooNarrow()
	{
		var html = Html("docs/testcases/TC.PW.01-Preview-Windows.md");
		var preview = Measure(PreviewPage(html), 300);
		var transcript = Measure(TranscriptPage(html), 300);
		if (preview == null || transcript == null)
			return;

		Assert.Contains(preview.Tables, x => x.Scrolls);
		Assert.Contains(transcript.Tables, x => x.Scrolls);
		Assert.False(preview.HostOverflow || preview.PageOverflow || transcript.HostOverflow || transcript.PageOverflow);
	}

	public sealed record TableReport(List<string> Bad, List<TableBox> Tables, bool HostOverflow, bool PageOverflow);

	public sealed record TableBox(int Cells, bool Scrolls, double BoxWidth);

	[EdgeTheory]
	[InlineData("docs/testcases/TC.PW.01-Preview-Windows.md", 1000)]
	[InlineData("docs/testcases/TC.PW.01-Preview-Windows.md", 420)]
	[InlineData("docs/features/preview-windows.md", 1000)]
	[InlineData("docs/features/preview-windows.md", 420)]
	public void Preview_EveryColumnKeepsItsLongestWord_AndAWideTableScrollsInItsOwnBox(string file, int width)
	{
		var report = Measure(PreviewPage(Html(file)), width);
		if (report == null)
			return;

		Assert.NotEmpty(report.Tables);
		Assert.Empty(report.Bad);
		Assert.False(report.HostOverflow, "the preview content scrolls sideways");
		Assert.False(report.PageOverflow, "the page scrolls sideways");
	}

	[EdgeTheory]
	[InlineData("docs/testcases/TC.PW.01-Preview-Windows.md", 900)]
	[InlineData("docs/testcases/TC.PW.01-Preview-Windows.md", 420)]
	[InlineData("docs/features/preview-windows.md", 900)]
	[InlineData("docs/features/preview-windows.md", 420)]
	public void Transcript_EveryColumnKeepsItsLongestWord_AndTheEntryNeverScrollsSideways(string file, int width)
	{
		var report = Measure(TranscriptPage(Html(file)), width);
		if (report == null)
			return;

		Assert.NotEmpty(report.Tables);
		Assert.Empty(report.Bad);
		Assert.False(report.HostOverflow, "the entry scrolls sideways");
		Assert.False(report.PageOverflow, "the page scrolls sideways");
	}
}