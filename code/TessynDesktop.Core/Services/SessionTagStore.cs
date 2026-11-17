using System;
using System.Collections.Generic;
using System.IO;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Reads and writes the Session Tag sidecar files under <c>%AppData%\TessynDesktop\tags</c> (FR.24.5): one
/// <c>&lt;NodeId&gt;.json</c> of declared tags per session plus <c>linear-workspaces.json</c>. Atomic writes
/// and move-aside of corrupt files come from <see cref="JsonSidecarFile"/>. Never deletes a file. Not
/// thread-safe by itself; <see cref="SessionTagService"/> serialises access.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagStore
{
	private readonly JsonSidecarFile _file = new();
	private readonly string _rootDir;

	public SessionTagStore() : this(GetDefaultRootDir())
	{
	}

	/// <summary>Constructor for testing: allows injecting a custom root directory.</summary>
	public SessionTagStore(string rootDir)
	{
		_rootDir = rootDir;
	}

	private static string GetDefaultRootDir()
	{
		return Path.Combine(Constants.DataRoot, Constants.SessionTags.FolderName);
	}

	/// <summary>The declared tags of a session; missing or unreadable (moved aside) means none.</summary>
	public List<SessionTagModel> LoadTags(string nodeId) =>
		_file.Read<SessionTagFileModel>(GetTagPath(nodeId))?.Tags ?? [];

	public void SaveTags(string nodeId, IReadOnlyList<SessionTagModel> tags) =>
		_file.Write(GetTagPath(nodeId), new SessionTagFileModel { Tags = [.. tags] });

	/// <summary>The Linear team key to workspace map; missing or unreadable (moved aside) means empty.</summary>
	public Dictionary<string, string> LoadWorkspaces() =>
		_file.Read<LinearWorkspaceMapModel>(GetWorkspacesPath())?.Workspaces ?? new Dictionary<string, string>();

	public void SaveWorkspaces(IReadOnlyDictionary<string, string> workspaces) =>
		_file.Write(GetWorkspacesPath(), new LinearWorkspaceMapModel { Workspaces = new Dictionary<string, string>(workspaces) });

	private string GetTagPath(string nodeId) =>
		Path.Combine(_rootDir, Path.GetFileName(nodeId) + Constants.Sidecar.FileExtension);

	private string GetWorkspacesPath() => Path.Combine(_rootDir, Constants.SessionTags.LinearWorkspacesFileName);
}
