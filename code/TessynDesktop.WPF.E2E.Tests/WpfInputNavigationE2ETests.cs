using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for input sources and keyboard navigation (FR.1.17): the BUILT WPF app against a real loopback daemon serving a real session file whose
/// USER entries carry sources (<c>source="scheduled"</c>, <c>source="orchestrated"</c>, and one legacy "[Scheduled]" entry without it). The transcript
/// page is driven through the DevTools port (<see cref="CdpPage"/>): real mouse clicks and key presses are sent to the page, and its state is read
/// back. Covers the teal colour and header labels, selecting an input, my-only versus all-inputs navigation, reaching an input beyond the loaded
/// page, and scrolling with nothing selected.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfInputNavigationE2ETests : IDisposable
{
	private const int Entries = 260;   // the first page holds the newest 100: entries 160..259
	private const int LegacyScheduled = 258;
	private const int Down = 40;
	private const int Up = 38;
	private const int PageDownKey = 34;
	private const int PageUpKey = 33;
	private const int Escape = 27;

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-input-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly string _main;

	public WpfInputNavigationE2ETests()
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

	/// <summary>What entry <paramref name="i"/> is: assistant on odd indexes; user entries are scheduled at i%12=4, orchestrated at i%12=8, otherwise mine.</summary>
	private static string KindOf(int i) => i % 2 == 1 ? "assistant" : i == LegacyScheduled ? "scheduled" : (i % 12) switch { 4 => "scheduled", 8 => "orchestrated", _ => "mine" };

	private static string Seed()
	{
		var sb = new StringBuilder();
		for (var i = 0; i < Entries; i++)
		{
			var kind = KindOf(i);
			if (kind == "assistant")
			{
				sb.Append(EntryIdsEnv.EntryText("ASSISTANT", i, $"answer {i}\n\nA few lines so that the entry has some height.\n\nSecond paragraph of answer {i}.\n\n- one\n- two\n- three", "model=\"opus\" effort=\"high\""));
				continue;
			}

			var meta = i == LegacyScheduled ? null : kind switch { "scheduled" => "source=\"scheduled\"", "orchestrated" => "source=\"orchestrated\"", _ => "profile=\"work\"" };
			var body = i == LegacyScheduled ? "[Scheduled] Written before sources were recorded" : $"input {i} ({kind})";
			sb.Append(EntryIdsEnv.EntryText("USER", i, body, meta));
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

	/// <summary>Scrolls the page so that entry <paramref name="i"/> is in the middle of the view, once it is rendered.</summary>
	private static async Task ShowEntryAsync(CdpPage page, int i)
	{
		await page.EvalAsync($"(() => {{ const k = posOfEntry({i}); vp.scrollTop = Math.max(0, S.off[k] - vp.clientHeight / 2); return true; }})()");
		for (var attempt = 0; attempt < 50; attempt++)
		{
			if ((await page.EvalAsync($"!!document.querySelector('.e[data-k=\"' + posOfEntry({i}) + '\"]')"))?.GetValue<bool>() == true)
				return;

			await Task.Delay(50);
		}

		Assert.Fail($"Entry {i} was not rendered");
	}

	/// <summary>A real click on the middle of the entry's text.</summary>
	private static async Task ClickEntryAsync(CdpPage page, int i)
	{
		await ShowEntryAsync(page, i);
		var rect = await page.EvalAsync($"(() => {{ const r = document.querySelector('.e[data-k=\"' + posOfEntry({i}) + '\"] .b').getBoundingClientRect(); return [r.left + 20, r.top + r.height / 2]; }})()");
		await page.ClickAsync(rect![0]!.GetValue<double>(), rect[1]!.GetValue<double>());
	}

	private static async Task<int> SelectedAsync(CdpPage page) => (int)(await Num("SEL === null ? -1 : SEL", page));

	private async Task ExpectSelectedAsync(CdpPage page, int expected)
	{
		await WaitAsync($"entry {expected} selected (selected: {await SelectedAsync(page)})", async () => await SelectedAsync(page) == expected);
		// and its bubble shows the dotted frame, once
		Assert.Equal(1, await Num("document.querySelectorAll('.c.sel').length", page));
	}

	// ── colour ────────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Scheduled_and_supervisor_inputs_are_teal_with_their_own_label_and_my_inputs_are_not()
	{
		var (process, page) = await StartAsync();
		try
		{
			// Every user entry the page has rendered: [entry index, source, has the auto class, header label, background colour].
			var rows = (await page.EvalAsync(@"Array.from(document.querySelectorAll('.e.U')).map(e => { const it = S.items[parseInt(e.dataset.k, 10)];
				return [it.i, it.s, e.classList.contains('auto'), e.querySelector('.h').textContent.split(' ')[0], getComputedStyle(e.querySelector('.c')).backgroundColor]; })"))!.AsArray();

			Assert.NotEmpty(rows);
			string? teal = null, plain = null;
			foreach (var row in rows)
			{
				var (i, source, auto, label) = (row![0]!.GetValue<int>(), row[1]!.GetValue<string>(), row[2]!.GetValue<bool>(), row[3]!.GetValue<string>());
				var expectAuto = KindOf(i) is "scheduled" or "orchestrated";
				Assert.True(auto == expectAuto, $"entry {i} ({KindOf(i)}, source '{source}'): auto class {auto}");
				Assert.Equal(!expectAuto ? "USER" : KindOf(i) == "scheduled" ? "SCHEDULED" : "SUPERVISOR", label);
				if (expectAuto) teal = row[4]!.GetValue<string>(); else plain = row[4]!.GetValue<string>();
			}

			Assert.Contains(rows, r => r![0]!.GetValue<int>() == LegacyScheduled);   // the legacy prefixed entry is among those checked
			Assert.NotNull(teal);
			Assert.NotNull(plain);
			Assert.NotEqual(plain, teal);
			var (r0, g0, b0) = Rgb(teal!);
			Assert.True(g0 > r0 && b0 > r0, $"the automated background {teal} is not teal (green and blue above red)");
		}
		finally
		{
			Stop(process, page);
		}
	}

	private static (int R, int G, int B) Rgb(string css)
	{
		// "rgb(205, 238, 234)", or for a colour made by color-mix() "color(srgb 0.8 0.93 0.91)" (channels 0..1).
		var parts = css[(css.IndexOf('(') + 1)..css.IndexOf(')')].Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
		var scale = 1.0;
		if (parts[0] == "srgb")
		{
			parts.RemoveAt(0);
			scale = 255;
		}

		var c = parts.Take(3).Select(p => (int)Math.Round(double.Parse(p, System.Globalization.CultureInfo.InvariantCulture) * scale)).ToArray();
		return (c[0], c[1], c[2]);
	}

	// ── selection ─────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task A_click_selects_an_input_with_a_thin_dotted_border_and_a_click_elsewhere_or_Esc_clears_it()
	{
		var (process, page) = await StartAsync();
		try
		{
			await ClickEntryAsync(page, 254);   // mine
			await ExpectSelectedAsync(page, 254);
			Assert.Equal("dotted", (await page.EvalAsync("getComputedStyle(document.querySelector('.c.sel')).outlineStyle"))!.GetValue<string>());
			Assert.Equal("1px", (await page.EvalAsync("getComputedStyle(document.querySelector('.c.sel')).outlineWidth"))!.GetValue<string>());

			await ClickEntryAsync(page, 256);   // scheduled: selectable too
			await ExpectSelectedAsync(page, 256);

			await ClickEntryAsync(page, 255);   // an answer: clears
			await WaitAsync("the selection cleared by a click on an answer", async () => await SelectedAsync(page) == -1);
			Assert.Equal(0, await Num("document.querySelectorAll('.c.sel').length", page));

			await ClickEntryAsync(page, 254);
			await ExpectSelectedAsync(page, 254);
			await page.PressAsync("Escape", Escape);
			await WaitAsync("the selection cleared by Esc", async () => await SelectedAsync(page) == -1);
			Assert.Equal(0, await Num("document.querySelectorAll('.c.sel').length", page));
		}
		finally
		{
			Stop(process, page);
		}
	}

	[Fact]
	public async Task A_drag_that_selects_text_does_not_change_the_selection()
	{
		var (process, page) = await StartAsync();
		try
		{
			await ClickEntryAsync(page, 254);
			await ExpectSelectedAsync(page, 254);

			await ShowEntryAsync(page, 250);
			var rect = (await page.EvalAsync("(() => { const r = document.querySelector('.e[data-k=\"' + posOfEntry(250) + '\"] .b p').getBoundingClientRect(); return [r.left + 1, r.right - 1, r.top + r.height / 2]; })()"))!.AsArray();
			await page.DragAsync(rect[0]!.GetValue<double>(), rect[2]!.GetValue<double>(), rect[1]!.GetValue<double>(), rect[2]!.GetValue<double>());

			Assert.True((await Num("window.getSelection().toString().length", page)) > 0, "the drag selected no text");
			Assert.Equal(254, await SelectedAsync(page));   // still the one clicked before
		}
		finally
		{
			Stop(process, page);
		}
	}

	// ── navigation ────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task From_one_of_mine_the_arrows_visit_my_inputs_only_and_from_an_automated_one_all_inputs()
	{
		var (process, page) = await StartAsync();
		try
		{
			// Mine: 168 -> 170 -> (172 scheduled skipped) 174 and back 170 -> 168 -> 166 -> (164 supervisor skipped) 162.
			await ClickEntryAsync(page, 168);
			await ExpectSelectedAsync(page, 168);
			foreach (var (key, code, expected) in new[] { ("ArrowDown", Down, 170), ("ArrowDown", Down, 174), ("ArrowUp", Up, 170), ("ArrowUp", Up, 168), ("ArrowUp", Up, 166), ("ArrowUp", Up, 162) })
			{
				await page.PressAsync(key, code);
				await ExpectSelectedAsync(page, expected);
				await ExpectNearTopAsync(page, expected);
			}

			// Automated: from the scheduled 172 down to 174 (mine), 176 (supervisor), 178; up again to 176, 174, 172, 170.
			await ClickEntryAsync(page, 172);
			await ExpectSelectedAsync(page, 172);
			foreach (var (key, code, expected) in new[] { ("ArrowDown", Down, 174), ("ArrowDown", Down, 176), ("ArrowDown", Down, 178), ("ArrowUp", Up, 176), ("ArrowUp", Up, 174), ("ArrowUp", Up, 172), ("ArrowUp", Up, 170) })
			{
				await page.PressAsync(key, code);
				await ExpectSelectedAsync(page, expected);
			}

			// From the supervisor input 176 the next input down is 178 (mine): any kind.
			await ClickEntryAsync(page, 176);
			await page.PressAsync("ArrowDown", Down);
			await ExpectSelectedAsync(page, 178);
		}
		finally
		{
			Stop(process, page);
		}
	}

	/// <summary>The selected entry's bubble is in the view, a little below its top edge (the 24 px margin; allow for the bottom clamp).</summary>
	private async Task ExpectNearTopAsync(CdpPage page, int i)
	{
		var offset = await Num("(() => { const c = document.querySelector('.c.sel'); return c.closest('.e').getBoundingClientRect().top - vp.getBoundingClientRect().top; })()", page);
		Assert.InRange(offset, 0, 60);
		Assert.Equal(i, await SelectedAsync(page));
	}

	[Fact]
	public async Task An_input_outside_the_loaded_page_is_reached_by_loading_the_older_page()
	{
		var (process, page) = await StartAsync();
		try
		{
			Assert.Equal(160, await Num("S.items[0].i", page));   // only the newest 100 entries are loaded
			await ClickEntryAsync(page, 162);                      // mine; the one before it is the scheduled 160, and before that 158 (mine) is on the older page
			await ExpectSelectedAsync(page, 162);

			await page.PressAsync("ArrowUp", Up);
			await ExpectSelectedAsync(page, 158);                  // mine only: the scheduled 160 is skipped, the older page was fetched
			Assert.True(await Num("S.items[0].i", page) < 160, "the older page was not loaded");
			await ExpectNearTopAsync(page, 158);

			await page.PressAsync("ArrowUp", Up);
			await ExpectSelectedAsync(page, 156);

			// And back down over the page boundary: the next of mine after 156 is 158 (the scheduled 160 is skipped only further on).
			await page.PressAsync("ArrowDown", Down);
			await ExpectSelectedAsync(page, 158);
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			Stop(process, page);
		}
	}

	// ── scrolling with nothing selected ───────────────────────────────────────────────────────

	[Fact]
	public async Task With_nothing_selected_the_arrows_scroll_a_line_and_PageUp_PageDown_a_page()
	{
		var (process, page) = await StartAsync();
		try
		{
			await ClickEntryAsync(page, 255);   // an answer: nothing is selected, and the page has the focus
			await WaitAsync("nothing selected", async () => await SelectedAsync(page) == -1);
			var height = await Num("vp.clientHeight", page);
			var pageStep = Math.Floor(height * 0.875);

			// Where a fixed point of the content sits is measured from an entry in the middle of the view.
			async Task<double> TopOfAnchor(int k) => await Num($"document.querySelector('.e[data-k=\"{k}\"]').getBoundingClientRect().top", page);
			async Task<int> AnchorAsync() => (int)await Num("(() => { const e = document.elementFromPoint(vp.clientWidth / 2, vp.clientHeight / 2); return parseInt(e.closest('.e').dataset.k, 10); })()", page);

			async Task<double> ShiftAfterAsync(string key, int code)
			{
				var anchor = await AnchorAsync();
				var before = await TopOfAnchor(anchor);
				await page.PressAsync(key, code);
				await Task.Delay(400);   // the scroll and the page's own re-render
				return await TopOfAnchor(anchor) - before;
			}

			Assert.InRange(await ShiftAfterAsync("PageUp", PageUpKey), pageStep - 3, pageStep + 3);   // content moves down by a page
			Assert.InRange(await ShiftAfterAsync("PageUp", PageUpKey), pageStep - 3, pageStep + 3);
			Assert.InRange(await ShiftAfterAsync("ArrowDown", Down), -42, -38);                       // content moves up by a line
			Assert.InRange(await ShiftAfterAsync("ArrowUp", Up), 38, 42);
			Assert.InRange(await ShiftAfterAsync("PageDown", PageDownKey), -pageStep - 3, -pageStep + 3);
			Assert.Equal(-1, await SelectedAsync(page));   // scrolling never selects
			Assert.Equal(0, await Num("document.querySelectorAll('.c.sel').length", page));
		}
		finally
		{
			Stop(process, page);
		}
	}
}
