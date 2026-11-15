using System;
using System.IO;
using System.Linq;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The real tree, session file service and JSONL importer over a temp directory, with a canned session index. Claude's projects folder is
/// <see cref="ProjectsDir"/>; <see cref="WriteClaudeSession"/> puts a session there the way Claude Code writes it.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ImportTestEnv : IDisposable
{
	public string Root { get; }
	public string ProjectsDir { get; }
	public AppSettingsService Settings { get; }
	public SessionFileService Files { get; }
	public SessionRegistry Registry { get; }
	public ClaudeSessionImportService Importer { get; }
	public InProcFakeIndexService Index { get; } = new();
	public ImportService Service { get; }

	public ImportTestEnv()
	{
		Root        = Path.Combine(Path.GetTempPath(), "cm_test_import_svc_" + Path.GetRandomFileName());
		ProjectsDir = Path.Combine(Root, "claude-projects");
		Directory.CreateDirectory(ProjectsDir);
		Settings = new AppSettingsService(Root);
		Settings.Settings.SessionFilesRoot = Path.Combine(Root, "sessions");
		Directory.CreateDirectory(Settings.Settings.SessionFilesRoot);
		Files    = new SessionFileService(Settings);
		Registry = new SessionRegistry(Settings, Files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		Importer = new ClaudeSessionImportService([ProjectsDir]);
		Service  = new ImportService(Importer, Index, Files, Registry, Settings, [ProjectsDir]);
	}

	public void Dispose()
	{
		try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
	}

	/// <summary>A folder under the root, added as a directory of the tree.</summary>
	public DirectoryNodeModel AddDirectory(string name) => Registry.AddDirectory(Path.Combine(Root, name));

	/// <summary>The Claude project folder of a working directory.</summary>
	public string SlugDirectoryOf(string workingDirectory) => Path.Combine(ProjectsDir, Constants.ClaudeSessions.BuildProjectSlug(workingDirectory));

	/// <summary>Writes a session of <paramref name="messages"/> user/assistant lines (all carrying <paramref name="cwd"/> when given) under the project folder <paramref name="slug"/>.</summary>
	public string WriteClaudeSession(string slug, string sessionId, string firstPrompt = "Hello", int messages = 2, string? cwd = null)
	{
		var directory = Path.Combine(ProjectsDir, slug);
		Directory.CreateDirectory(directory);
		var path  = Path.Combine(directory, sessionId + Constants.ClaudeSessions.SessionFileExtension);
		var lines = new System.Collections.Generic.List<string>();
		var cwdPart = cwd == null ? string.Empty : $",\"cwd\":{System.Text.Json.JsonSerializer.Serialize(cwd)}";
		for (var i = 0; i < messages; i++)
		{
			var stamp = $"2026-03-10T10:00:{i:00}Z";
			lines.Add(i % 2 == 0
				? $$"""{"type":"user","message":{"role":"user","content":{{System.Text.Json.JsonSerializer.Serialize(i == 0 ? firstPrompt : "more " + i)}}},"timestamp":"{{stamp}}","uuid":"u{{i}}","sessionId":"{{sessionId}}"{{cwdPart}}}"""
				: $$"""{"type":"assistant","message":{"content":[{"type":"text","text":"answer {{i}}"}]},"timestamp":"{{stamp}}","uuid":"a{{i}}","sessionId":"{{sessionId}}"{{cwdPart}}}""");
		}

		File.WriteAllLines(path, lines, new UTF8Encoding(false));
		return path;
	}

	/// <summary>The summary of a Claude session file, as the picker holds it.</summary>
	public ClaudeSessionSummaryModel SummaryOf(string jsonlPath) =>
		Importer.ExtractSummary(Path.GetFileNameWithoutExtension(jsonlPath), jsonlPath)!;

	public string[] SessionFiles() => Directory.GetFiles(Settings.Settings.SessionFilesRoot, "*.txt");
}
