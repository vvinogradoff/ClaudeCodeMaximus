using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// Everything the preview-window E2E tests (TC.PW.01) share: a real loopback daemon over a <see cref="FocusRefsEnv"/> whose main session's newest entry mentions
/// four files (two markdown, one C++, one TIFF) and a link to a tiny local web server, a second session, a pre-written <c>wpf.json</c> (the preview sections) and
/// the built app started with <c>--test-window --test-monitors n</c>. Windows are found and driven by their own handle, because the app now has several top-level windows
/// (the main window, the preview windows, Settings).
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class PreviewWindowsHarness : IDisposable
{
	public const int Ctrl = 2;   // DevTools modifier bit
	public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-preview-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly CancellationTokenSource _stopWeb = new();
	private readonly ClientTestHost _host;

	public PreviewWindowsHarness(bool splitGroups = false, bool autoClose = false, string? previewWindowsJson = null, string? previewsJson = null)
	{
		Directory.CreateDirectory(_root);
		Env = new FocusRefsEnv();
		Md1 = Env.WriteFile("docs/plan1.md", "# Plan one\n\nFirst markdown file.\n");
		Md2 = Env.WriteFile("docs/plan2.md", "# Plan two\n\nSecond markdown file.\n");
		Cpp = Env.WriteFile("src/main.cpp", "int main() { return 0; }\n");
		Tiff = Env.WriteFile("shots/pic.tiff", MakeTiff());

		WebPort = StartWeb();
		WebUrlA = $"http://127.0.0.1:{WebPort}/a";
		WebUrlB = $"http://127.0.0.1:{WebPort}/b";

		Env.WriteBodies(
			"what did you produce?",
			$"Plan one is {Md1} and plan two is {Md2}; the code is {Cpp} and a picture {Tiff}. See [the page]({WebUrlA}) for more.");
		Main = Env.Session.NodeId;
		Second = Env.AddSession("Second session", "other question", "other answer").NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(Env.Base.Registry, new StubStreamingManager(), Env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(Env.Base.Settings), Env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. Env.Modules, tree, new CatalogTestModule([])]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var sections = new List<string>();
		if (previewsJson != null)
			sections.Add($"\"previews\":{{\"version\":1,\"data\":{previewsJson}}}");
		else if (splitGroups)
			sections.Add($"\"previews\":{{\"version\":1,\"data\":{SplitGroups(autoClose)}}}");
		if (previewWindowsJson != null)
			sections.Add($"\"previewWindows\":{{\"version\":1,\"data\":{previewWindowsJson}}}");
		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		StateFile = Path.Combine(clients.FullName, "wpf.json");
		File.WriteAllText(StateFile,
			$"{{\"lastSelectedNodeId\":\"{Main}\",\"expandedNodeIds\":[\"{Env.Base.Directory.NodeId}\"],\"sections\":{{{string.Join(",", sections)}}}}}");
		App = new AppProcessHarness(_root, daemonFile);
	}

	public FocusRefsEnv Env { get; }

	/// <summary>Tells the connected client that the Focus items of the main session changed, as the daemon's notification publisher does after the attention service changed one (the test host does not wire it).</summary>
	public void PublishAttentionChanged() => _host.Bus.Publish("attention.changed", new TessynDesktop.Models.Remote.AttentionChangedPayload(Main, null!));

	public AppProcessHarness App { get; }

	public string Md1 { get; }

	public string Md2 { get; }

	public string Cpp { get; }

	public string Tiff { get; }

	public string Main { get; }

	public string Second { get; }

	public int WebPort { get; }

	public string WebUrlA { get; }

	public string WebUrlB { get; }

	public string StateFile { get; }

	/// <summary>A group list where Markdown and C++ are two groups and a "Local" group takes the local web server; <paramref name="autoClose"/> is the auto-close switch of Markdown and Local.</summary>
	public string SplitGroups(bool autoClose = false)
	{
		var flag = autoClose ? "true" : "false";
		return "{\"groups\":[" +
			"{\"id\":\"images\",\"name\":\"Images\",\"patterns\":\"jpg jpeg png gif webp bmp tif tiff\",\"autoClose\":false}," +
			$"{{\"id\":\"text-code\",\"name\":\"Text and code\",\"patterns\":\"md txt\",\"autoClose\":{flag}}}," +
			"{\"id\":\"cpp\",\"name\":\"C++\",\"patterns\":\"cpp h\",\"autoClose\":false}," +
			$"{{\"id\":\"local\",\"name\":\"Local\",\"patterns\":\"http://127.0.0.1:{WebPort}/*\",\"autoClose\":{flag}}}]}}";
	}

	// ---- the app ----

	/// <summary>Starts the app with <paramref name="monitors"/> scripted monitors; with a CDP port the transcript page can be driven.</summary>
	public async Task<Process> StartAsync(int monitors, int? cdpPort = null)
	{
		var env = cdpPort == null ? new Dictionary<string, string>() : CdpPage.EnvironmentFor(cdpPort.Value);
		var process = App.Start(env, "--test-monitors", monitors.ToString());
		Assert.True(await App.WaitForLogAsync(process, StartTimeout, $"Session {Main} opened"), "The session did not open. Log:\n" + App.ReadLog());
		return process;
	}

	/// <summary>Starts with the transcript page connected and its paths rendered inline.</summary>
	public async Task<(Process Process, CdpPage Page)> StartWithPageAsync(int monitors)
	{
		var port = CdpPage.FreePort();
		var process = await StartAsync(monitors, port);
		var page = await CdpPage.ConnectAsync(port, "transcript.html", StartTimeout);
		await WaitUntilAsync("the four paths to render inline", async () => (int)(await page.EvalAsync("document.querySelectorAll('.pth').length"))! == 4);
		return (process, page);
	}

	/// <summary>Closes the app the way the user does: the close message goes to the MAIN window (the first window of the process may be a preview window).</summary>
	public void CloseApp(Process process)
	{
		if (process.HasExited)
			return;

		var main = MainWindow(process);
		if (main != IntPtr.Zero)
			PostMessage(main, 0x0010, IntPtr.Zero, IntPtr.Zero);

		if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
			process.Kill(entireProcessTree: true);
	}

	public void Dispose()
	{
		_stopWeb.Cancel();
		try
		{
			_webListener?.Close();
		}
		catch (ObjectDisposedException)
		{
			// already closed
		}

		_host.Dispose();
		Env.Dispose();
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
			// WebView2 helper processes can hold the data folder for a moment; the folder is in %TEMP%.
		}
	}

	public void AssertNoErrors()
	{
		var log = App.ReadLog();
		var lines = log.ReplaceLineEndings().Split(Environment.NewLine);
		var errors = string.Join(Environment.NewLine, lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("[ERR]", StringComparison.Ordinal) || x.l.Contains("[FTL]", StringComparison.Ordinal))
			.Select(x => string.Join(Environment.NewLine, lines.Skip(x.i).Take(8))));
		Assert.True(errors.Length == 0, "The log has errors:" + Environment.NewLine + errors);
	}

	// ---- the windows ----

	/// <summary>The main window: the one with the title bar's "Main menu" button.</summary>
	public IntPtr MainWindow(Process process) => new AppWindowFinder().FindAll(process.Id).FirstOrDefault(h => FindElement(h, "Main menu") != null);

	/// <summary>The open preview windows (title and handle): every top-level window of the app with a "Close preview" button except the main window (whose Focus pane has one too).</summary>
	public List<(IntPtr Handle, string Title)> PreviewWindows(Process process)
	{
		var main = MainWindow(process);
		var found = new List<(IntPtr, string)>();
		foreach (var hwnd in new AppWindowFinder().FindAll(process.Id))
		{
			if (hwnd == main || FindElement(hwnd, "Close preview") == null)
				continue;

			found.Add((hwnd, TitleOf(hwnd)));
		}

		return found;
	}

	public string[] PreviewTitles(Process process) => PreviewWindows(process).Select(w => w.Title).OrderBy(t => t, StringComparer.Ordinal).ToArray();

	/// <summary>Waits until the preview windows have exactly these titles (in any order).</summary>
	public async Task WaitForTitlesAsync(Process process, params string[] expected)
	{
		var want = expected.OrderBy(t => t, StringComparer.Ordinal).ToArray();
		await WaitUntilAsync("the preview windows [" + string.Join(" | ", want) + "], now [" + string.Join(" | ", PreviewTitles(process)) + "]",
			() => Task.FromResult(PreviewTitles(process).SequenceEqual(want)));
	}

	public IntPtr PreviewWindowTitled(Process process, string title) =>
		PreviewWindows(process).FirstOrDefault(w => w.Title == title).Handle;

	public static string TitleOf(IntPtr hwnd)
	{
		try
		{
			return AutomationElement.FromHandle(hwnd).Current.Name;
		}
		catch (ElementNotAvailableException)
		{
			return string.Empty;
		}
	}

	// ---- UI Automation, by window handle ----

	public static AutomationElement? FindElement(IntPtr hwnd, string name)
	{
		try
		{
			return AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException)
		{
			return null;
		}
	}

	/// <summary>The first element with this name in any window of the app (the main window, a preview window, Settings, a menu popup).</summary>
	public static (IntPtr Window, AutomationElement Element)? FindAnywhere(Process process, string name)
	{
		foreach (var hwnd in new AppWindowFinder().FindAll(process.Id))
		{
			var element = FindElement(hwnd, name);
			if (element != null)
				return (hwnd, element);
		}

		return null;
	}

	public static bool TryInvoke(AutomationElement element)
	{
		try
		{
			if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
			{
				((InvokePattern)invoke).Invoke();
				return true;
			}

			if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
			{
				((TogglePattern)toggle).Toggle();
				return true;
			}

			if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
			{
				((SelectionItemPattern)select).Select();
				return true;
			}
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException)
		{
			return false;
		}

		return false;
	}

	/// <summary>Waits for the element with this name in this window and presses it (Invoke, Toggle or Select, whichever it offers).</summary>
	public async Task<bool> PressAsync(Process process, IntPtr hwnd, string name, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? StepTimeout);
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var element = FindElement(hwnd, name);
			if (element != null && TryInvoke(element))
				return true;

			await Task.Delay(200);
		}

		return false;
	}

	/// <summary>Like <see cref="PressAsync"/>, in whichever window of the app has the element.</summary>
	public async Task<bool> PressAnywhereAsync(Process process, string name, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? StepTimeout);
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var found = FindAnywhere(process, name);
			if (found != null && TryInvoke(found.Value.Element))
				return true;

			await Task.Delay(200);
		}

		return false;
	}

	public static ToggleState? ToggleStateOf(IntPtr hwnd, string name)
	{
		try
		{
			var element = FindElement(hwnd, name);
			return element != null && element.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern) ? ((TogglePattern)pattern).Current.ToggleState : null;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
		{
			return null;
		}
	}

	public static bool IsEnabled(IntPtr hwnd, string name)
	{
		try
		{
			return FindElement(hwnd, name)?.Current.IsEnabled ?? false;
		}
		catch (ElementNotAvailableException)
		{
			return false;
		}
	}

	/// <summary>Sets the text of the edit with this name in whichever window has it.</summary>
	public async Task<bool> SetTextAnywhereAsync(Process process, string name, string text)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var found = FindAnywhere(process, name);
			try
			{
				if (found != null && found.Value.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
				{
					((ValuePattern)pattern).SetValue(text);
					return true;
				}
			}
			catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException)
			{
				// the window is changing: look again
			}

			await Task.Delay(200);
		}

		return false;
	}

	/// <summary>Sets the text of the edit whose name starts with <paramref name="prefix"/> and whose text is <paramref name="currentText"/> (a new group row has an empty name, and UI Automation may trim the name's trailing space).</summary>
	public async Task<bool> SetTextOfEditAsync(Process process, string prefix, string currentText, string text)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			foreach (var hwnd in new AppWindowFinder().FindAll(process.Id))
			{
				try
				{
					foreach (AutomationElement edit in AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)))
					{
						if (!(edit.Current.Name.StartsWith(prefix, StringComparison.Ordinal) || edit.Current.Name.Length == 0) || !edit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
							continue;

						var value = (ValuePattern)pattern;
						if (value.Current.Value == currentText)
						{
							value.SetValue(text);
							return true;
						}
					}
				}
				catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException)
				{
					// the window is changing: look again
				}
			}

			await Task.Delay(200);
		}

		return false;
	}

	/// <summary>Names and values of every edit of the app's windows, for a failure message.</summary>
	public static string DumpEdits(Process process)
	{
		var text = new StringBuilder();
		foreach (var hwnd in new AppWindowFinder().FindAll(process.Id))
		{
			try
			{
				foreach (AutomationElement edit in AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)))
					text.Append($"[{edit.Current.Name}={(edit.TryGetCurrentPattern(ValuePattern.Pattern, out var p) ? ((ValuePattern)p).Current.Value : "?")}] ");
			}
			catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
			{
				text.Append("(window changing) ");
			}
		}

		return text.ToString();
	}

	public static bool TextShown(Process process, string fragment)
	{
		foreach (var hwnd in new AppWindowFinder().FindAll(process.Id))
		{
			try
			{
				var any = AutomationElement.FromHandle(hwnd)
					.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
					.Cast<AutomationElement>()
					.Any(e => e.Current.Name.Contains(fragment, StringComparison.Ordinal));
				if (any)
					return true;
			}
			catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
			{
				// the window is changing
			}
		}

		return false;
	}

	/// <summary>Selects the session row of the main window's tree.</summary>
	public async Task<bool> SelectSessionAsync(Process process, string rowName)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var main = MainWindow(process);
			if (main != IntPtr.Zero)
			{
				try
				{
					var row = AutomationElement.FromHandle(main).FindFirst(TreeScope.Descendants, new AndCondition(
						new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
						new PropertyCondition(AutomationElement.NameProperty, rowName)));
					if (row != null && row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
					{
						((SelectionItemPattern)pattern).Select();
						return true;
					}
				}
				catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
				{
					// the tree is changing: look again
				}
			}

			await Task.Delay(250);
		}

		return false;
	}

	// ---- the stored settings ----

	/// <summary>The data of a settings section of <c>wpf.json</c> (null when there is none yet).</summary>
	public JsonNode? Section(string name)
	{
		try
		{
			using var stream = new FileStream(StateFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			var root = JsonNode.Parse(stream);
			return root?["sections"]?[name]?["data"];
		}
		catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
		{
			return null;
		}
	}

	/// <summary>Waits (the store writes a moment after a change) until the section satisfies the predicate.</summary>
	public async Task<JsonNode?> WaitForSectionAsync(string name, Func<JsonNode?, bool> predicate, string what)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			var data = Section(name);
			if (predicate(data))
				return data;

			await Task.Delay(200);
		}

		Assert.Fail($"Expected: {what}. Section {name} is {Section(name)?.ToJsonString() ?? "missing"}. Log:\n{App.ReadLog()}");
		return null;
	}

	// ---- the transcript page ----

	/// <summary>Ctrl+clicks the n-th path or link of the transcript text.</summary>
	public static async Task CtrlClickAsync(CdpPage page, string selector, int index)
	{
		var (x, y) = await PointOfAsync(page, selector, index);
		await page.MouseMoveAsync(x, y, modifiers: Ctrl);
		await page.ClickAsync(x, y, modifiers: Ctrl);
	}

	public static async Task ClickAsync(CdpPage page, string selector, int index)
	{
		var (x, y) = await PointOfAsync(page, selector, index);
		await page.ClickAsync(x, y);
	}

	private static async Task<(double X, double Y)> PointOfAsync(CdpPage page, string selector, int index)
	{
		var script = $"(() => {{ const e = document.querySelectorAll({JsonValue.Create(selector)!.ToJsonString()})[{index}]; e.scrollIntoView({{ block: 'center' }}); const r = e.getClientRects()[0]; return [r.left, r.right, r.top, r.bottom]; }})()";
		await page.EvalAsync(script);
		await Task.Delay(150);
		var a = (JsonArray)(await page.EvalAsync(script))!;
		var left = a[0]!.GetValue<double>();
		var right = a[1]!.GetValue<double>();
		return (left + Math.Min(6, (right - left) / 2), (a[2]!.GetValue<double>() + a[3]!.GetValue<double>()) / 2);
	}

	// ---- waiting ----

	public async Task WaitUntilAsync(string what, Func<Task<bool>> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (await condition())
				return;

			await Task.Delay(150);
		}

		Assert.Fail($"Expected: {what}. Log:\n{App.ReadLog()}");
	}

	/// <summary>Waits for the log to contain all the needles; fails with the log otherwise.</summary>
	public async Task WaitForLogAsync(Process process, string what, params string[] needles) =>
		Assert.True(await App.WaitForLogAsync(process, StepTimeout, needles), $"Expected: {what}. Log:\n{App.ReadLog()}");

	// ---- the little web server ----

	private int StartWeb()
	{
		for (var attempt = 0; attempt < 20; attempt++)
		{
			var port = CdpPage.FreePort();
			var listener = new HttpListener();
			listener.Prefixes.Add($"http://127.0.0.1:{port}/");
			try
			{
				listener.Start();
			}
			catch (HttpListenerException)
			{
				continue;
			}

			_ = Task.Run(() => ServeAsync(listener));
			_webListener = listener;
			return port;
		}

		throw new InvalidOperationException("No free port for the test web server.");
	}

	private HttpListener? _webListener;

	private async Task ServeAsync(HttpListener listener)
	{
		while (!_stopWeb.IsCancellationRequested)
		{
			HttpListenerContext context;
			try
			{
				context = await listener.GetContextAsync();
			}
			catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
			{
				return;
			}

			var path = context.Request.Url?.AbsolutePath ?? "/";
			var title = path.TrimStart('/').ToUpperInvariant();
			var body = Encoding.UTF8.GetBytes($"<!doctype html><html><head><title>Page {title}</title></head><body><h1>Page {title}</h1></body></html>");
			context.Response.ContentType = "text/html; charset=utf-8";
			context.Response.ContentLength64 = body.Length;
			await context.Response.OutputStream.WriteAsync(body);
			context.Response.Close();
		}
	}

	// ---- a TIFF by hand ----

	/// <summary>A 2x2 8-bit grey uncompressed little-endian TIFF, built by hand so the test needs no imaging library.</summary>
	private static byte[] MakeTiff()
	{
		var bytes = new List<byte> { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00 };
		void U16(int v) { bytes.Add((byte)v); bytes.Add((byte)(v >> 8)); }
		void U32(int v) { U16(v & 0xFFFF); U16(v >> 16); }
		void Entry(int tag, int type, int count, int value) { U16(tag); U16(type); U32(count); if (type == 3) { U16(value); U16(0); } else U32(value); }

		const int entries = 9;
		var dataAt = 8 + 2 + (entries * 12) + 4;
		U16(entries);
		Entry(256, 3, 1, 2);        // ImageWidth
		Entry(257, 3, 1, 2);        // ImageLength
		Entry(258, 3, 1, 8);        // BitsPerSample
		Entry(259, 3, 1, 1);        // Compression: none
		Entry(262, 3, 1, 1);        // Photometric: BlackIsZero
		Entry(273, 4, 1, dataAt);   // StripOffsets
		Entry(277, 3, 1, 1);        // SamplesPerPixel
		Entry(278, 3, 1, 2);        // RowsPerStrip
		Entry(279, 4, 1, 4);        // StripByteCounts
		U32(0);                      // no next IFD
		bytes.AddRange([0x00, 0x80, 0xC0, 0xFF]);
		return bytes.ToArray();
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
