using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for Copy as Markdown (FR.2.13, UC.CM.01, TC.CM.02/04/05): the BUILT WPF app against a loopback daemon whose session holds a rich assistant
/// response (heading, link, list, table, code block) and a second answer. The page is driven over the DevTools port with real right clicks, clicks and key
/// presses; the clipboard is read back from the machine's own clipboard, so what the host really wrote is what is checked.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfCopyAsMarkdownE2ETests : IDisposable
{
	private const int CtrlShift = 2 | 8;   // DevTools modifier bits

	private const string Rich =
		"## Plan\n\nIntro with **bold** and a [link](https://example.com/docs).\n\n- first item\n- second item\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n\n```csharp\nvar x = 1;\nvar y = 2;\n```\n\nClosing line.";

	private const string Second = "Second **answer**\n\n- one";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-copymd-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly string _main;

	public WpfCopyAsMarkdownE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies("show the plan", Rich, "thanks", Second);
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

	[Fact]
	public async Task TC_CM_04_A_right_click_on_a_selection_opens_the_menu_without_one_or_on_a_link_it_does_not()
	{
		var (process, page) = await StartAsync();
		try
		{
			// No selection: nothing opens.
			await page.EvalAsync("getSelection().removeAllRanges()");
			var heading = await PointOfAsync(page, "h2", 0);
			await page.RightClickAsync(heading.X, heading.Y);
			Assert.False(await MenuOpenAsync(page), "a menu opened without a selection");

			// A selection from the heading into the code block: the menu opens with the two items.
			await SelectAsync(page, "h2", "pre code", 5);
			heading = await PointOfAsync(page, "h2", 0);
			await page.RightClickAsync(heading.X, heading.Y);
			Assert.True(await MenuOpenAsync(page), "the menu did not open on the selection");
			Assert.Equal("Copy|Copy as Markdown", string.Join("|", ((JsonArray)(await page.EvalAsync("Array.from(document.querySelectorAll('.cm button:not([hidden])')).map(b => b.firstChild.textContent)"))!).Select(n => n!.GetValue<string>())));
			Assert.Contains("Plan", (string)(await page.EvalAsync("getSelection().toString()"))!);

			// The selection survives the menu; Esc closes it and keeps the selection.
			await page.PressAsync("Escape", 27);
			Assert.False(await MenuOpenAsync(page), "Esc did not close the menu");
			Assert.False((bool)(await page.EvalAsync("getSelection().isCollapsed"))!, "the selection was lost");

			// On a link inside the selection the menu adds "Open in system browser" (FR.26.9) to the Copy entries.
			var link = await PointOfAsync(page, ".b a", 0);
			await page.RightClickAsync(link.X, link.Y);
			Assert.True(await MenuOpenAsync(page), "no menu opened on a link inside the selection");
			Assert.Equal("Open in system browser|Copy|Copy as Markdown", string.Join("|", ((JsonArray)(await page.EvalAsync("Array.from(document.querySelectorAll('.cm button:not([hidden])')).map(b => b.firstChild.textContent)"))!).Select(n => n!.GetValue<string>())));
			AssertNoErrors();
		}
		finally
		{
			Stop(process, page);
		}
	}

	[Fact]
	public async Task TC_CM_05_Copy_puts_plain_text_and_Copy_as_Markdown_and_Ctrl_Shift_C_put_the_Markdown()
	{
		var (process, page) = await StartAsync();
		try
		{
			// Copy as Markdown, from the menu: heading to the middle of the code block.
			await SelectAsync(page, "h2", "pre code", 10);
			ClearClipboard();
			await OpenMenuAsync(page);
			await ClickMenuAsync(page, "markdown");
			var markdown = await WaitClipboardAsync();
			Assert.StartsWith("## Plan", markdown);
			Assert.Contains("**bold**", markdown);
			Assert.Contains("[link](https://example.com/docs)", markdown);
			Assert.Contains("- first item\n- second item", markdown);
			Assert.Contains("| a | b |\n| --- | --- |\n| 1 | 2 |", markdown);
			Assert.Contains("```csharp\nvar x = 1", markdown);
			Assert.DoesNotContain("var y", markdown);
			Assert.DoesNotContain("Closing line", markdown);

			// Copy, from the menu: plain text.
			ClearClipboard();
			await OpenMenuAsync(page);
			await ClickMenuAsync(page, "text");
			var plain = await WaitClipboardAsync();
			Assert.Contains("Plan", plain);
			Assert.DoesNotContain("##", plain);
			Assert.DoesNotContain("**", plain);
			Assert.DoesNotContain("```", plain);

			// Ctrl+Shift+C: the same as Copy as Markdown.
			ClearClipboard();
			await page.PressAsync("C", 67, CtrlShift);
			Assert.Equal(markdown, await WaitClipboardAsync());
			AssertNoErrors();
		}
		finally
		{
			Stop(process, page);
		}
	}

	[Fact]
	public async Task TC_CM_02_A_selection_over_whole_entries_copies_their_original_markdown_with_a_blank_line_between()
	{
		var (process, page) = await StartAsync();
		try
		{
			// From the start of the first answer to the end of the second one: three whole entries (answer, the user's thanks, answer).
			await page.EvalAsync("(() => { const b = Array.from(document.querySelectorAll('.e .b')), r = document.createRange(); r.setStart(b[1], 0); r.setEnd(b[3], b[3].childNodes.length); const s = getSelection(); s.removeAllRanges(); s.addRange(r); return 1; })()");
			ClearClipboard();
			var first = await PointOfAsync(page, "h2", 0);
			await page.RightClickAsync(first.X, first.Y);
			Assert.True(await MenuOpenAsync(page), "the menu did not open");
			await ClickMenuAsync(page, "markdown");

			Assert.Equal(Rich + "\n\nthanks\n\n" + Second, await WaitClipboardAsync());
			AssertNoErrors();
		}
		finally
		{
			Stop(process, page);
		}
	}

	private async Task<(Process Process, CdpPage Page)> StartAsync()
	{
		var port = CdpPage.FreePort();
		var process = _app.Start(CdpPage.EnvironmentFor(port));
		Assert.True(await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint"), "The app did not open the session. Log:\n" + _app.ReadLog());
		var page = await CdpPage.ConnectAsync(port, "transcript.html", StartTimeout);
		await WaitAsync("the four entries to render", async () => (int)(await page.EvalAsync("document.querySelectorAll('.e .b').length"))! == 4 && (bool)(await page.EvalAsync("!!document.querySelector('pre code')"))!);
		await Task.Delay(300);
		return (process, page);
	}

	private void Stop(Process process, CdpPage page)
	{
		page.Dispose();
		_app.Stop(process);
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	/// <summary>Selects from the start of the first <paramref name="from"/> element to <paramref name="offset"/> characters into the text of the first <paramref name="to"/> element.</summary>
	private static Task SelectAsync(CdpPage page, string from, string to, int offset) =>
		page.EvalAsync($"(() => {{ const a = document.querySelector({JsonValue.Create(from)!.ToJsonString()}), b = document.querySelector({JsonValue.Create(to)!.ToJsonString()}), r = document.createRange(); r.setStart(a.firstChild, 0); r.setEnd(b.firstChild, {offset}); const s = getSelection(); s.removeAllRanges(); s.addRange(r); return 1; }})()");

	private static async Task<bool> MenuOpenAsync(CdpPage page)
	{
		await Task.Delay(150);
		return (bool)(await page.EvalAsync("!document.querySelector('.cm').hidden"))!;
	}

	/// <summary>A real right click on the selection's first line.</summary>
	private static async Task OpenMenuAsync(CdpPage page)
	{
		var point = await PointOfAsync(page, "h2", 0);
		await page.RightClickAsync(point.X, point.Y);
		Assert.True(await MenuOpenAsync(page), "the menu did not open on the selection");
	}

	private static async Task ClickMenuAsync(CdpPage page, string act)
	{
		var a = (JsonArray)(await page.EvalAsync($"(() => {{ const r = document.querySelector('.cm button[data-act=\"{act}\"]').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; }})()"))!;
		await page.ClickAsync(a[0]!.GetValue<double>(), a[1]!.GetValue<double>());
	}

	private static async Task<(double X, double Y)> PointOfAsync(CdpPage page, string selector, int index)
	{
		var script = $"(() => {{ const e = document.querySelectorAll({JsonValue.Create(selector)!.ToJsonString()})[{index}]; e.scrollIntoView({{ block: 'center' }}); const r = e.getClientRects()[0]; return [r.left + Math.min(8, r.width / 2), (r.top + r.bottom) / 2]; }})()";
		await page.EvalAsync(script);
		await Task.Delay(150);
		var a = (JsonArray)(await page.EvalAsync(script))!;
		return (a[0]!.GetValue<double>(), a[1]!.GetValue<double>());
	}

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

	private async Task<string> WaitClipboardAsync()
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		string? text = null;
		while (DateTime.UtcNow < deadline)
		{
			text = OnStaThread(() => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null);
			if (!string.IsNullOrEmpty(text))
				return text.Replace("\r\n", "\n");

			await Task.Delay(150);
		}

		Assert.Fail("Nothing reached the clipboard. Log:\n" + _app.ReadLog());
		return string.Empty;
	}

	private static void ClearClipboard() => OnStaThread<string?>(() =>
	{
		System.Windows.Clipboard.Clear();
		return null;
	});

	/// <summary>The clipboard is an STA resource and another process may hold it for a moment: a few tries.</summary>
	private static T? OnStaThread<T>(Func<T?> work)
	{
		T? result = default;
		var thread = new Thread(() =>
		{
			for (var attempt = 0; attempt < 10; attempt++)
			{
				try
				{
					result = work();
					return;
				}
				catch (COMException)
				{
					Thread.Sleep(100);
				}
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		return result;
	}
}
