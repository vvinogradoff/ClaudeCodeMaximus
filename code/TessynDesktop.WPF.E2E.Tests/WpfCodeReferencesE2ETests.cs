using System.Diagnostics;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for Code references (FR.7.13, FR.7.14, FR.4.8; TC.CR.23 and TC.CR.24): the BUILT WPF app against a real loopback daemon with the real
/// autocomplete module, the real settings sections over the real settings service, and a registry holding the sessions of the test. With the setting
/// off (the default), <c>#alpha te</c> offers sessions and choosing one writes <c>session "name" (shortid)</c>; after the box is ticked in Settings
/// (no restart) <c>#Fo</c> offers symbols. The rows are buttons chosen through UI Automation, as in <see cref="WpfComposerCompletionE2ETests"/>: the
/// keyboard keys (Down, Enter, Esc) have no automation here and are covered by the view-model tests.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfCodeReferencesE2ETests : IDisposable
{
	private const string InputName = "Prompt input";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-coderefs-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly DraftService _drafts;
	private readonly FakeCodeIndexService _codeIndex = new();
	private readonly AutocompleteRpcModule _autocomplete;
	private readonly SettingsSectionService _sections;
	private readonly SessionNodeModel _alphaTests;
	private readonly string _main;

	public WpfCodeReferencesE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, 4).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;

		// Sessions of the fixture S of TC.CR.01 that the test needs (the same project as the open session, so one group of sessions without a group node).
		var registry = _env.Base.Registry;
		var group = registry.AddGroup(_env.Base.Directory, "G1");
		_alphaTests = registry.AddSessionToGroup(group, "Alpha tests");
		registry.AddSessionToGroup(group, "Beta alpha");

		_drafts = new DraftService(Path.Combine(_root, "drafts"));
		_codeIndex.SetReady();
		_codeIndex.Symbols =
		[
			new CodeSymbolModel { Name = "Foo", FullyQualifiedName = "Foo", Namespace = "My.Space", Kind = CodeSymbolKind.Class, FilePath = "Foo.cs", Line = 3 },
			new CodeSymbolModel { Name = "FooBar", FullyQualifiedName = "FooBar", Namespace = "My.Space", Kind = CodeSymbolKind.Class, FilePath = "FooBar.cs", Line = 9 },
		];
		Assert.False(_env.Base.Settings.Settings.CodeReferencesEnabled);   // the default: off
		_autocomplete = new AutocompleteRpcModule(registry, _codeIndex, _env.Base.Settings);
		_sections = new SettingsSectionService(_env.Base.Settings, [new GeneralSettingsSection(), new NotificationsSettingsSection()]);

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(registry, TestEngines.Claude(new StubStreamingManager()), _env.Attention, activity, new SessionAuthErrorTracker());
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher(
		[
			new ClientRpcModule(runInfo: run, eventLog: log, drafts: _drafts, codeIndex: _codeIndex, settingsSections: _sections),
			.. _env.Modules, tree, new CatalogTestModule([]), new DraftRpcModule(registry, _drafts), _autocomplete, new SettingsRpcModule(_sections),
		]));

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
		_autocomplete.Dispose();
		_sections.Dispose();
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

	private async Task<Process> StartAsync()
	{
		var process = _app.Start();
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened");
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task<string> WaitForInputAsync(Process process, Func<string, bool> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		string? text = null;
		while (DateTime.UtcNow < deadline)
		{
			text = _clicker.GetText(process, InputName);
			if (text != null && condition(text))
				return text;

			await Task.Delay(250);
		}

		Assert.Fail($"The prompt input never reached the expected text; it shows '{text}'. Log:\n" + _app.ReadLog());
		return string.Empty;
	}

	private async Task TypeAsync(Process process, string text)
	{
		Assert.True(await _clicker.SetTextAsync(process, InputName, text, StepTimeout));
		Assert.True(await _clicker.PlaceCaretAtEndAsync(process, InputName, StepTimeout), "The caret could not be placed. Log:\n" + _app.ReadLog());
	}

	[Fact]
	public async Task TCCR23_WithCodeReferencesOff_HashOffersSessions_AndChoosingOneWritesTheReference()
	{
		var shortId = new SessionListIndex(_env.Base.Registry.SnapshotSessionsForListing()).GetShortId(_alphaTests.NodeId);
		using var process = await StartAsync();
		try
		{
			await TypeAsync(process, "see #alpha te");

			Assert.True(await _clicker.WaitForButtonAsync(process, "Alpha tests", StepTimeout), "The session suggestions did not appear. Log:\n" + _app.ReadLog());
			Assert.False(_clicker.Exists(process, "Beta alpha"));            // does not contain "alpha te"
			Assert.False(_clicker.TextExists(process, "Building the code index"));
			Assert.True(await _clicker.ClickAsync(process, "Alpha tests", StepTimeout));

			var text = await WaitForInputAsync(process, t => t.Contains("session \"", StringComparison.Ordinal));
			Assert.Equal($"see session \"Alpha tests\" ({shortId})", text);
			Assert.True(await _clicker.WaitForButtonGoneAsync(process, "Alpha tests", StepTimeout), "The popup stayed open.");
			Assert.Equal(0, _codeIndex.BuildStarts);                         // no index was started for sessions
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task TCCR24_AfterTickingCodeReferencesInSettings_HashOffersSymbols_WithoutARestart()
	{
		using var process = await StartAsync();
		try
		{
			// Off first: sessions, not symbols.
			await TypeAsync(process, "see #Fo");
			Assert.False(_clicker.Exists(process, "FooBar"));

			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Settings…", StepTimeout), "The Settings entry was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.SelectTabAsync(process, "General tab", StepTimeout), "The General tab was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ToggleCheckBoxAsync(process, "Code references", StepTimeout), "The Code references box was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Save settings", StepTimeout));

			var deadline = DateTime.UtcNow + StepTimeout;
			while (!_env.Base.Settings.Settings.CodeReferencesEnabled && DateTime.UtcNow < deadline)
				await Task.Delay(100);
			Assert.True(_env.Base.Settings.Settings.CodeReferencesEnabled, "The daemon did not get the setting. Log:\n" + _app.ReadLog());

			Assert.True(await _clicker.ClickAsync(process, "Close settings", StepTimeout));
			Assert.True(await _clicker.WaitForButtonGoneAsync(process, "Close settings", StepTimeout));

			// Retype: setting the very same text again is no edit (the composer asks only when the text or the caret changes), and a test window's
			// Settings never takes the keyboard focus, so the input's lost-focus close that resets the composer in real use does not happen here.
			await TypeAsync(process, string.Empty);
			await TypeAsync(process, "see #Fo");
			Assert.True(await _clicker.WaitForButtonAsync(process, "FooBar", StepTimeout), "The symbol suggestions did not appear. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Foo", StepTimeout));

			var text = await WaitForInputAsync(process, t => t.Contains("My.Space.Foo", StringComparison.Ordinal));
			Assert.Equal("see My.Space.Foo", text);
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
