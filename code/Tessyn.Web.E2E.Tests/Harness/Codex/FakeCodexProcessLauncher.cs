using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TessynDesktop.Codex;

// Copied from TessynDesktop.Tests/Codex/Fakes for the Tessyn Web E2E harness (FR.33, TC.CX.42): this project cannot
// reference the test project, so the in-process fake Codex app-server is compiled here too. Keep the two copies in step.
namespace Tessyn.Web.E2E.Tests.Harness.Codex;

/// <summary>
/// The test <see cref="ICodexProcessLauncher"/> (FR.33.19): every launch creates and starts a new <see cref="FakeCodexAppServer"/> on
/// in-memory pipes and records the launch spec. <see cref="Configure"/> sets each new server up before it starts.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeCodexProcessLauncher : ICodexProcessLauncher
{
	private readonly ConcurrentQueue<FakeCodexAppServer> _servers = new();
	private int _nextPid = 40000;

	public FakeCodexScripts Scripts { get; } = new();

	public Action<FakeCodexAppServer>? Configure { get; set; }

	public IReadOnlyList<FakeCodexAppServer> Servers => _servers.ToList();

	public FakeCodexAppServer Latest => _servers.Last();

	public IReadOnlyList<CodexLaunchSpec> Launches => _servers.Select(s => s.Spec).ToList();

	public ICodexProcess Launch(CodexLaunchSpec spec)
	{
		var server = new FakeCodexAppServer(spec, Interlocked.Increment(ref _nextPid), Scripts);
		Configure?.Invoke(server);
		_servers.Enqueue(server);
		server.Start();
		return server.Process;
	}
}
