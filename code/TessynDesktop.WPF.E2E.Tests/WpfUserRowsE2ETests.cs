using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the Architect's own inputs in the session output (ISSUE.495): the BUILT WPF app opens a session file that is a slice of REAL data
/// (<c>TestData/real-user-entries.txt</c>: four USER entries with <c>profile="[Sys] ..."</c>, copied from a real session, the original untouched).
/// The transcript page is read over the DevTools port: every user row exists, is displayed, has a real size, and its text has contrast against the
/// background it sits on, so a row collapsed to nothing, hidden or painted in the page colour fails.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfUserRowsE2ETests : IDisposable
{
	private const int UserEntries = 4;
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-userrows-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly string _main;

	public WpfUserRowsE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.Base.WriteRaw(File.ReadAllText(RealSlice()));
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, new CatalogTestModule([])]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_main}\",\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"]}}");
		_app = new AppProcessHarness(_root, daemonFile);
	}

	public void Dispose()
	{
		_host.Dispose();
		_env.Dispose();
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
			// WebView2 helper processes can hold the data folder for a moment; the folder is in %TEMP%.
		}
	}

	private static string RealSlice([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "TestData", "real-user-entries.txt");

	private async Task<(Process Process, CdpPage Page)> StartAsync()
	{
		var port = CdpPage.FreePort();
		var process = _app.Start(CdpPage.EnvironmentFor(port));
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
		var page = await CdpPage.ConnectAsync(port, "transcript.html", StartTimeout);
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && (await page.EvalAsync("S && S.items ? S.items.length : 0"))?.GetValue<double>() < 8)
			await Task.Delay(100);
		await Task.Delay(500);
		return (process, page);
	}

	[Fact]
	public async Task Every_user_row_of_real_data_is_displayed_with_a_size_and_readable_text()
	{
		var (process, page) = await StartAsync();
		try
		{
			// Per user row: exists, display, size of the row and of the bubble, text vs background colour (nearest opaque background up the tree).
			var rows = (await page.EvalAsync(@"(() => {
				function parse(c) { const srgb = c.startsWith('color(srgb'), m = c.match(/[\d.]+/g).map(Number), k = srgb ? 255 : 1, o = 0; return { r: m[o] * k, g: m[o + 1] * k, b: m[o + 2] * k, a: m.length > o + 3 ? m[o + 3] : 1 }; }
				function lum(c) { const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); }
				function bg(el) { for (let e = el; e; e = e.parentElement) { const c = parse(getComputedStyle(e).backgroundColor); if (c.a > 0.5) return c; } return { r: 255, g: 255, b: 255, a: 1 }; }
				return Array.from(document.querySelectorAll('.e.U')).map(e => { const c = e.querySelector('.c'), b = e.querySelector('.b'), cr = c.getBoundingClientRect(), br = b.getBoundingClientRect();
					const fg = parse(getComputedStyle(b).color), back = bg(c), l1 = lum(fg), l2 = lum(back), page = bg(document.body);
					return { text: b.textContent.length, display: getComputedStyle(e).display, vis: getComputedStyle(e).visibility, cw: cr.width, ch: cr.height, bw: br.width, bh: br.height,
						contrast: (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05), bubbleVsPage: Math.abs(lum(back) - lum(page)) }; }); })()"))!.AsArray();

			Assert.Equal(UserEntries, rows.Count);
			foreach (var row in rows)
			{
				var o = row!.AsObject();
				Assert.True(o["text"]!.GetValue<int>() > 20, "the bubble has no text: " + o);
				Assert.NotEqual("none", o["display"]!.GetValue<string>());
				Assert.Equal("visible", o["vis"]!.GetValue<string>());
				Assert.True(o["cw"]!.GetValue<double>() > 100 && o["ch"]!.GetValue<double>() > 15, "the bubble collapsed: " + o);
				Assert.True(o["bw"]!.GetValue<double>() > 100 && o["bh"]!.GetValue<double>() > 10, "the text collapsed: " + o);
				Assert.True(o["contrast"]!.GetValue<double>() >= 4.5, "the text does not contrast with its background: " + o);
			}

			Assert.Equal(0, (await page.EvalAsync("document.querySelectorAll('.e .e').length"))!.GetValue<int>());
		}
		finally
		{
			page.Dispose();
			_app.Stop(process);
		}
	}
}
