using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the look of assistant responses and for the scrollbar of the transcript page: the BUILT WPF app against a real loopback daemon serving a
/// session of 100 entries of very different heights (so the estimated heights of the entries that were never rendered are wrong). The page is driven
/// through the DevTools port (<see cref="CdpPage"/>). Covers: an assistant response has no border, no background and no role word and carries its time on
/// the right while user bubbles keep their look; dragging the scrollbar thumb follows the mouse one to one and the content under it does not jump when
/// entries get measured.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfTranscriptScrollE2ETests : IDisposable
{
	private const int Entries = 100;   // one page: no paging happens while the thumb is dragged
	private const int SystemEntry = 20;   // a system entry among the user ones

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-scroll-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly string _main;

	public WpfTranscriptScrollE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.Base.WriteRaw(Seed());
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

	/// <summary>User entries on even indexes (short), assistant entries on odd ones with 1 to 40 paragraphs, so real heights differ widely from any estimate.</summary>
	private static string Seed()
	{
		var sb = new StringBuilder();
		for (var i = 0; i < Entries; i++)
		{
			if (i == SystemEntry)
			{
				sb.Append(EntryIdsEnv.EntryText("SYSTEM", i, "Read notes.md"));
				continue;
			}

			if (i % 2 == 0)
			{
				sb.Append(EntryIdsEnv.EntryText("USER", i, $"input {i}", "profile=\"work\""));
				continue;
			}

			var lines = 1 + (i * 7) % 40;
			var body = string.Join("\n\n", Enumerable.Range(0, lines).Select(l => $"Paragraph {l} of answer {i}, a line of text that is long enough to wrap now and then when the window is narrow."));
			sb.Append(EntryIdsEnv.EntryText("ASSISTANT", i, body, "model=\"opus\" effort=\"high\""));
		}

		return sb.ToString();
	}

	private async Task<(Process Process, CdpPage Page)> StartAsync()
	{
		var port = CdpPage.FreePort();
		var process = _app.Start(CdpPage.EnvironmentFor(port));
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());

		var page = await CdpPage.ConnectAsync(port, "transcript.html", StartTimeout);
		await WaitAsync("the transcript page to show the newest entries", async () => await Num("S && S.items.length ? S.items[S.items.length - 1].i : -1", page) == Entries - 1);
		await SettleAsync(page);
		return (process, page);
	}

	private void Stop(Process process, CdpPage page)
	{
		page.Dispose();
		_app.Stop(process);
	}

	private static async Task<double> Num(string expression, CdpPage page) => (await page.EvalAsync(expression))?.GetValue<double>() ?? double.NaN;

	private async Task WaitAsync(string what, Func<Task<bool>> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (await condition())
				return;

			await Task.Delay(100);
		}

		Assert.Fail($"Expected: {what}. Log:\n{_app.ReadLog()}");
	}

	/// <summary>Waits until the scroll position, the content height, the rendered range and the thumb stop changing (three equal samples).</summary>
	private static async Task SettleAsync(CdpPage page)
	{
		var last = "";
		for (int stable = 0, i = 0; stable < 3 && i < 100; i++)
		{
			var now = (await page.EvalAsync("vp.scrollTop + ':' + S.off[S.items.length] + ':' + S.a + ':' + S.b + ':' + sbt.getBoundingClientRect().top"))?.ToString() ?? "";
			stable = now == last ? stable + 1 : 0;
			last = now;
			await Task.Delay(40);
		}
	}

	[Fact]
	public async Task Assistant_responses_sit_on_the_view_without_border_background_or_role_word_with_their_time_on_the_right_and_user_bubbles_keep_their_look()
	{
		var (process, page) = await StartAsync();
		try
		{
			var a = (await page.EvalAsync(@"(() => { const e = Array.from(document.querySelectorAll('.e.A')).pop(); const c = e.querySelector('.c'), t = e.querySelector('.t'), cs = getComputedStyle(c);
				return [cs.backgroundColor, cs.borderTopWidth, cs.borderLeftWidth, e.textContent.toUpperCase().includes('ASSISTANT'), t ? t.textContent : null, t ? t.getBoundingClientRect().left - c.getBoundingClientRect().right : -999, t ? t.getBoundingClientRect().left - e.getBoundingClientRect().left : -999, e.getBoundingClientRect().width]; })()"))!.AsArray();
			Assert.Equal("rgba(0, 0, 0, 0)", a[0]!.GetValue<string>());
			Assert.Equal("0px", a[1]!.GetValue<string>());
			Assert.Equal("0px", a[2]!.GetValue<string>());
			Assert.False(a[3]!.GetValue<bool>(), "the word ASSISTANT is printed");
			Assert.Matches(@"^\d\d:\d\d:\d\d$", a[4]!.GetValue<string>());
			Assert.True(a[5]!.GetValue<double>() >= -0.5, "the time is not to the right of the text: " + a[5]);
			Assert.True(a[6]!.GetValue<double>() > a[7]!.GetValue<double>() * 0.8, "the time is not at the right edge: " + a[6] + " of " + a[7]);

			var u = (await page.EvalAsync(@"(() => { const e = document.querySelector('.e.U'); const c = e.querySelector('.c'), cs = getComputedStyle(c), ts = e.querySelector('.ts'), d = ts.querySelector('.d'), tm = ts.querySelector('.tm');
				return [cs.backgroundColor, e.textContent.includes('USER'), d.textContent, tm.textContent, ts.getBoundingClientRect().right <= c.getBoundingClientRect().left + 0.5, tm.getBoundingClientRect().top >= d.getBoundingClientRect().bottom - 0.5]; })()"))!.AsArray();
			Assert.NotEqual("rgba(0, 0, 0, 0)", u[0]!.GetValue<string>());
			Assert.False(u[1]!.GetValue<bool>(), "the word USER is printed");
			Assert.Matches(@"^\d{4}-\d\d-\d\d$", u[2]!.GetValue<string>());
			Assert.Matches(@"^\d\d:\d\d$", u[3]!.GetValue<string>());
			Assert.True(u[4]!.GetValue<bool>(), "the date and time are not left of the bubble");
			Assert.True(u[5]!.GetValue<bool>(), "the time is not under the date");
		}
		finally
		{
			Stop(process, page);
		}
	}

	[Fact]
	public async Task System_rows_have_no_role_word_the_time_on_the_right_and_the_pale_blue_of_the_palette_in_both_themes()
	{
		var (process, page) = await StartAsync();
		try
		{
			// The seeded system entry may be outside the rendered window: render it by scrolling there.
			await page.EvalAsync($"vp.scrollTop = S.off[{SystemEntry} - S.items[0].i]; 0");
			await WaitAsync("the system row to be rendered", async () => await Num("document.querySelectorAll('.e.S').length", page) >= 1);
			const string Probe = @"(() => { const e = document.querySelector('.e.S'); const c = e.querySelector('.c'), t = e.querySelector('.t');
				return [getComputedStyle(c).backgroundColor, e.textContent.toUpperCase().includes('SYSTEM'), t ? t.textContent : null, t ? t.getBoundingClientRect().left - c.getBoundingClientRect().right : -999, t ? t.getBoundingClientRect().left - e.getBoundingClientRect().left : -999, e.getBoundingClientRect().width, document.documentElement.getAttribute('data-theme')]; })()";
			var s = (await page.EvalAsync(Probe))!.AsArray();
			var dark = s[6]?.GetValue<string>() == "dark";
			Assert.Equal(dark ? "rgb(26, 58, 80)" : "rgb(191, 224, 247)", s[0]!.GetValue<string>());
			Assert.False(s[1]!.GetValue<bool>(), "the word SYSTEM is printed");
			Assert.Matches(@"^\d\d:\d\d:\d\d$", s[2]!.GetValue<string>());
			Assert.True(s[3]!.GetValue<double>() >= -0.5, "the time is not to the right of the text: " + s[3]);
			Assert.True(s[4]!.GetValue<double>() > s[5]!.GetValue<double>() * 0.8, "the time is not at the right edge");

			// The page takes the palette colour from the host's appearance message, for either theme.
			await page.EvalAsync("onAppearance({ theme: 'dark', sys: '#112233' }); 0");
			Assert.Equal("rgb(17, 34, 51)", (await page.EvalAsync("getComputedStyle(document.querySelector('.e.S .c')).backgroundColor"))!.GetValue<string>());
			await page.EvalAsync("onAppearance({ theme: 'light', sys: '#BFE0F7' }); 0");
			Assert.Equal("rgb(191, 224, 247)", (await page.EvalAsync("getComputedStyle(document.querySelector('.e.S .c')).backgroundColor"))!.GetValue<string>());
		}
		finally
		{
			Stop(process, page);
		}
	}

	/// <summary>TC.LS.07 (FR.9.12): the one line-spacing value drives every row of the page; a change from the host re-measures the virtualised list.</summary>
	[Fact]
	public async Task Line_spacing_applies_to_every_row_kind_and_a_change_re_measures_the_list()
	{
		var (process, page) = await StartAsync();
		try
		{
			// Ratio of the computed line height to the font size of the newest assistant text, a user bubble and the root.
			const string Ratios = @"(() => { const r = el => { const cs = getComputedStyle(el); return parseFloat(cs.lineHeight) / parseFloat(cs.fontSize); };
				const a = Array.from(document.querySelectorAll('.e.A .c')).pop(), u = document.querySelector('.e.U .c');
				return [r(a), r(u), r(document.body)]; })()";
			var first = (await page.EvalAsync(Ratios))!.AsArray();
			foreach (var ratio in first)
				Assert.Equal(1.25, ratio!.GetValue<double>(), 2);   // the stored default reached the page

			const string AvgHeight = "items.getBoundingClientRect().height / items.children.length";   // the rendered entries, whatever the window of them is
			var height0 = await Num(AvgHeight, page);
			await page.EvalAsync("onAppearance({ theme: 'dark', asst: 14, md: 14, user: 14, lh: 1.6 }); 0");
			foreach (var ratio in (await page.EvalAsync(Ratios))!.AsArray())
				Assert.Equal(1.6, ratio!.GetValue<double>(), 2);
			await WaitAsync("the list to be measured again with the wider spacing", async () => await Num(AvgHeight, page) > height0 * 1.05);

			var height1 = await Num(AvgHeight, page);
			await page.EvalAsync("onAppearance({ theme: 'dark', asst: 14, md: 14, user: 14, lh: 1.05 }); 0");
			foreach (var ratio in (await page.EvalAsync(Ratios))!.AsArray())
				Assert.Equal(1.05, ratio!.GetValue<double>(), 2);
			await WaitAsync("the list to be measured again with the tighter spacing", async () => await Num(AvgHeight, page) < height1 * 0.9);
		}
		finally
		{
			Stop(process, page);
		}
	}

	[Fact]
	public async Task Dragging_the_scrollbar_thumb_follows_the_mouse_one_to_one_and_the_content_does_not_jump_when_entries_get_measured()
	{
		var (process, page) = await StartAsync();
		try
		{
			// The page opens at the bottom with the entries above it never measured (estimated heights).
			Assert.True(await Num("document.querySelectorAll('#sb:not(.off)').length", page) == 1, "the scrollbar is not shown");
			var travel = await Num("sb.clientHeight - sbt.getBoundingClientRect().height", page);
			Assert.True(travel > 100, "the thumb has no room to travel: " + travel);

			var thumb = (await page.EvalAsync("(() => { const r = sbt.getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2, r.top]; })()"))!.AsArray();
			double x = thumb[0]!.GetValue<double>(), y = thumb[1]!.GetValue<double>(), top0 = thumb[2]!.GetValue<double>();
			const double Up = -0.7;   // of the travel, in five steps

			await page.MouseDownAsync(x, y);
			var topEntries = new List<double>();
			for (var step = 1; step <= 5; step++)
			{
				var dy = Up * travel * step / 5;
				await page.MouseMoveHeldAsync(x, y + dy);
				await Task.Delay(120);   // entries are rendered and measured meanwhile
				var top = await Num("sbt.getBoundingClientRect().top", page);
				Assert.True(Math.Abs(top - (top0 + dy)) < 1.5, $"step {step}: the thumb is at {top}, the mouse asked for {top0 + dy}");
				topEntries.Add(await Num("ScrollMap.entryPos(S.off, vp.scrollTop)", page));
			}

			// The content shows the entry the thumb's position stands for: the entry-space position is the thumb's fraction of the position at the bottom.
			var f = (top0 + Up * travel) / travel;   // the track starts at the top of the page
			var expected = Math.Max(0, Math.Min(1, f)) * await Num("ScrollMap.entryPos(S.off, vp.scrollHeight - vp.clientHeight)", page);
			Assert.True(Math.Abs(topEntries[^1] - expected) < 0.6, $"the view is at entry {topEntries[^1]}, the thumb stands for {expected}");

			await page.MouseUpAsync(x, y + Up * travel);
			var before = await Num("ScrollMap.entryPos(S.off, vp.scrollTop)", page);
			await SettleAsync(page);
			var after = await Num("ScrollMap.entryPos(S.off, vp.scrollTop)", page);
			Assert.True(Math.Abs(before - after) < 0.05, $"the content moved after the drag ended: entry {before} -> {after}");
			Assert.True(Math.Abs(topEntries[^1] - after) < 0.05, $"the content moved between the last mouse move and the end: {topEntries[^1]} -> {after}");

			// Dragging all the way down lands exactly at the bottom, all the way up exactly at the top.
			var mid = await Num("(() => { const r = sbt.getBoundingClientRect(); return r.top + r.height / 2; })()", page);
			await page.MouseDownAsync(x, mid);
			await page.MouseMoveHeldAsync(x, mid + travel * 2);
			await Task.Delay(150);
			await SettleAsync(page);
			Assert.True(await Num("vp.scrollHeight - vp.clientHeight - vp.scrollTop", page) < 1, "the thumb at the bottom does not show the bottom");
			await page.MouseMoveHeldAsync(x, mid - travel * 2);
			await Task.Delay(150);
			await page.MouseUpAsync(x, mid - travel * 2);
			await SettleAsync(page);
			Assert.True(await Num("vp.scrollTop", page) < 1, "the thumb at the top does not show the top");
		}
		finally
		{
			Stop(process, page);
		}
	}
}
