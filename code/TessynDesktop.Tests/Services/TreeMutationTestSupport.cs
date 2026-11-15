using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary><see cref="IDirectoryGitOrigins"/> fake: canned origins per directory path, and a refresh that completes at once (or is "already running").</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeDirectoryGitOrigins : IDirectoryGitOrigins
{
	public Dictionary<string, string?> Origins { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>When true, <see cref="StartRefresh"/> answers "a refresh is already running".</summary>
	public bool RefreshBusy { get; set; }

	public int Refreshes { get; private set; }

	public string? GetOrigin(string directoryPath) => Origins.TryGetValue(directoryPath, out var origin) ? origin : null;

	public Task? StartRefresh(IReadOnlyList<string> directoryPaths, Action completed)
	{
		if (RefreshBusy)
			return null;

		Refreshes++;
		completed();
		return Task.CompletedTask;
	}
}

/// <summary>
/// The real tree (temp-dir settings and <see cref="SessionRegistry"/>) with the J1 tree mutation service on top, a fake git-origin cache and
/// a counter of the registry's tree change events.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TreeMutationTestEnv : IDisposable
{
	public string Root { get; }
	public AppSettingsService Settings { get; }
	public FakeSessionFileService Files { get; } = new();
	public SessionRegistry Registry { get; }
	public FakeDirectoryGitOrigins Origins { get; } = new();
	public TreeMutationService Tree { get; }

	/// <summary>The breadth rules of the environment: its own home folder and data root, so the tests do not depend on the machine's profile.</summary>
	public BroadDirectoryRules Broad { get; }

	public string Home { get; }
	public string DataRoot { get; }

	/// <summary>The structural tree change events raised so far (everything but running-state changes and the desktop re-projection marker).</summary>
	public int TreeChanges { get; private set; }

	public TreeMutationTestEnv()
	{
		Root     = Path.Combine(Path.GetTempPath(), "cm_test_j1_" + Path.GetRandomFileName());
		Directory.CreateDirectory(Root);
		Settings = new AppSettingsService(Root);
		Registry = new SessionRegistry(Settings, Files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		Home     = Path.Combine(Root, "home");
		DataRoot = Path.Combine(Root, "data-root");
		Broad    = new BroadDirectoryRules([DataRoot], [Home]);
		Tree     = new TreeMutationService(Registry, Settings, new FakeDirectoryLabelService(), Origins, Broad);
		Registry.TreeChanged += (_, e) =>
		{
			if (e.Kind is not (TreeChangeKind.SessionIsRunningChanged or TreeChangeKind.DaemonMutation))
				TreeChanges++;
		};
	}

	public void Dispose() => Directory.Delete(Root, recursive: true);

	/// <summary>Creates the folder on disk and adds it as a directory.</summary>
	public DirectoryNodeModel AddDirectory(string name)
	{
		var path = Path.Combine(Root, name);
		Directory.CreateDirectory(path);
		return Registry.AddDirectory(path);
	}

	/// <summary>Adds a session, optionally as a worker of <paramref name="supervisorNodeId"/>.</summary>
	public SessionNodeModel AddSession(DirectoryNodeModel dir, string name, GroupNodeModel? group = null, string? supervisorNodeId = null)
	{
		var session = group == null ? Registry.AddSession(dir, name) : Registry.AddSessionToGroup(group, name);
		lock (Settings.SyncRoot)
			session.SupervisorNodeId = supervisorNodeId;
		return session;
	}

	/// <summary>The names of the sessions directly in a directory's or group's list, in order.</summary>
	public static string[] Names(List<SessionNodeModel> sessions) => [.. sessions.ConvertAll(s => s.Name)];
}
