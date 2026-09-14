using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Threading;
using TessynDesktop.Models;
using TessynDesktop.Services;
using ReactiveUI;
using Serilog;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Owns the displayed message log for a single session: loading from the .txt/JSONL files,
/// watching both for external changes, and swapping between the TessynDesktop and Claude CLI
/// JSONL views (FR.11.7). Extracted from SessionViewModel (Stage 7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionContentViewModel : ReactiveObject, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<SessionContentViewModel>();

	private readonly ISessionFileService _fileService;
	private readonly IClaudeSessionImportService _importService;
	private readonly SessionNodeViewModel _node;
	private readonly Func<bool> _isBusyProvider;
	private bool _isMarkdownMode = true;
	private bool _isClaudeSessionView;
	private List<MessageEntryViewModel>? _tessynMessagesSnapshot;
	private FileSystemWatcher? _fileWatcher;
	private FileSystemWatcher? _jsonlWatcher;
	private Timer? _fileChangeDebounceTimer;
	private Timer? _jsonlChangeDebounceTimer;
	private Timer? _externalActivityTimeoutTimer;
	private int _lastKnownEntryCount;

	/// <summary>
	/// Safety-net window for <see cref="SessionNodeViewModel.IsExternallyActive"/>: if the external
	/// process dies mid-turn without ever appending a terminal "result" entry (e.g. a crash), the
	/// JSONL watcher never fires again and the flag would otherwise stay stuck true forever. 45s
	/// comfortably exceeds normal inter-chunk gaps during streaming while still recovering quickly.
	/// </summary>
	private static readonly TimeSpan ExternalActivityTimeout = TimeSpan.FromSeconds(45);

	public ObservableCollection<MessageEntryViewModel> Messages { get; } = [];

	public bool IsMarkdownMode
	{
		get => _isMarkdownMode;
		set => this.RaiseAndSetIfChanged(ref _isMarkdownMode, value);
	}

	/// <summary>When true, displays messages from Claude CLI's JSONL session instead of the TessynDesktop .txt file.</summary>
	public bool IsClaudeSessionView
	{
		get => _isClaudeSessionView;
		set
		{
			if (_isClaudeSessionView == value) return;
			this.RaiseAndSetIfChanged(ref _isClaudeSessionView, value);
			SwapMessageSource(value);
		}
	}

	/// <summary>True when the session has any linked Claude JSONL file(s) that can be viewed.</summary>
	public bool HasClaudeSession => !string.IsNullOrEmpty(_node.Model.ClaudeSessionId)
	                                || _node.Model.PriorClaudeSessionIds.Count > 0;

	public SessionContentViewModel(
		ISessionFileService fileService,
		IClaudeSessionImportService importService,
		SessionNodeViewModel node,
		Func<bool> isBusyProvider)
	{
		_fileService     = fileService;
		_importService   = importService;
		_node            = node;
		_isBusyProvider  = isBusyProvider;
	}

	/// <summary>Appends a system-role message with the current timestamp.</summary>
	public void AddSystemMessage(string content) =>
		Messages.Add(new MessageEntryViewModel
		{
			Role      = Constants.SessionFile.RoleSystem,
			Content   = content,
			Timestamp = DateTimeOffset.UtcNow,
		});

	/// <summary>Raises a change notification for <see cref="HasClaudeSession"/> after the owning node's ClaudeSessionId/PriorClaudeSessionIds change.</summary>
	public void NotifyClaudeSessionChanged() => this.RaisePropertyChanged(nameof(HasClaudeSession));

	public void LoadMessagesFromFile()
	{
		var entries = _fileService.ReadEntries(_node.FileName);
		foreach (var entry in entries)
		{
			if (entry.Role != Constants.SessionFile.RoleCompaction
			    && string.IsNullOrWhiteSpace(entry.Content))
				continue;

			Messages.Add(EntryToViewModel(entry));
		}

		// Initialize JSONL entry count for the watcher. Must reflect the JSONL's current
		// state, not the .txt file's, because the JSONL typically has more entries (tool_use, etc.)
		// and RefreshFromJsonl compares against this to find only truly new entries.
		_lastKnownEntryCount = InitializeJsonlEntryCount();
		StartFileWatcher();
	}

	/// <summary>
	/// Counts the current displayable entries in the JSONL file so the watcher
	/// can detect only truly new entries. Falls back to Messages.Count if no JSONL exists.
	/// </summary>
	public int InitializeJsonlEntryCount()
	{
		var sessionId = _node.Model.ClaudeSessionId;
		if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(_node.Model.WorkingDirectory))
			return Messages.Count;

		try
		{
			var jsonlPath = BuildJsonlPathForSessionId(sessionId);
			if (jsonlPath == null || !File.Exists(jsonlPath))
				return Messages.Count;

			var entries = _importService.ParseJsonlSession(jsonlPath);
			var count = 0;
			foreach (var entry in entries)
			{
				if (entry.Role != Constants.SessionFile.RoleCompaction
				    && !string.IsNullOrWhiteSpace(entry.Content))
					count++;
			}

			_log.Debug("InitializeJsonlEntryCount: {Count} displayable entries in JSONL for session {SessionId}",
				count, sessionId);
			return count;
		}
		catch (Exception ex)
		{
			_log.Debug("InitializeJsonlEntryCount: error reading JSONL: {Error}", ex.Message);
			return Messages.Count;
		}
	}

	private void StartFileWatcher()
	{
		// Watch the Tessyn .txt session file
		try
		{
			var fullPath = _fileService.GetFullPath(_node.FileName);
			var directory = Path.GetDirectoryName(fullPath);
			var fileName = Path.GetFileName(fullPath);

			if (directory != null && Directory.Exists(directory))
			{
				_fileWatcher = new FileSystemWatcher(directory, fileName)
				{
					NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
					EnableRaisingEvents = true,
				};
				_fileWatcher.Changed += OnSessionFileChanged;
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Failed to start .txt file watcher for session {FileName}", _node.FileName);
		}

		// Watch the Claude Code JSONL file (for sessions running externally)
		StartJsonlWatcher();
	}

	private void StartJsonlWatcher()
	{
		var sessionId = _node.Model.ClaudeSessionId;
		if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(_node.Model.WorkingDirectory))
			return;

		try
		{
			// Prefer wherever the JSONL already lives (any profile, active or removed); fall back
			// to the OS-default location for a session that hasn't written one yet.
			var existingPath = Constants.ClaudeSessions.FindExistingJsonlPath(_node.Model.WorkingDirectory, sessionId);

			string jsonlDir;
			string jsonlFileName;
			if (existingPath != null)
			{
				jsonlDir = Path.GetDirectoryName(existingPath)!;
				jsonlFileName = Path.GetFileName(existingPath);
			}
			else
			{
				var slug = Constants.ClaudeSessions.BuildProjectSlug(_node.Model.WorkingDirectory);
				var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				jsonlDir = Path.Combine(
					userProfile,
					Constants.ClaudeSessions.ClaudeHomeFolderName,
					Constants.ClaudeSessions.ProjectsFolderName,
					slug);
				jsonlFileName = sessionId + Constants.ClaudeSessions.SessionFileExtension;
			}

			if (!Directory.Exists(jsonlDir))
				return;

			_log.Debug("Starting JSONL watcher for {Path}", Path.Combine(jsonlDir, jsonlFileName));

			_jsonlWatcher = new FileSystemWatcher(jsonlDir, jsonlFileName)
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
				EnableRaisingEvents = true,
			};
			_jsonlWatcher.Changed += OnJsonlFileChanged;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Failed to start JSONL watcher for session {SessionId}", sessionId);
		}
	}

	private void OnSessionFileChanged(object sender, FileSystemEventArgs e)
	{
		// Ignore changes caused by our own writes (IsBusy covers all write paths)
		if (_isBusyProvider())
			return;

		_fileChangeDebounceTimer?.Dispose();
		_fileChangeDebounceTimer = new Timer(
			_ => RefreshFromFile(),
			null,
			500,
			Timeout.Infinite);
	}

	private void OnJsonlFileChanged(object sender, FileSystemEventArgs e)
	{
		// Ignore changes when we're actively sending (our own process writes to the JSONL too)
		if (_isBusyProvider())
			return;

		// Quick check: read the last line to detect if Claude is actively working
		DetectExternalActivity(e.FullPath);

		_jsonlChangeDebounceTimer?.Dispose();
		_jsonlChangeDebounceTimer = new Timer(
			_ => RefreshFromJsonl(e.FullPath),
			null,
			1000,
			Timeout.Infinite);
	}

	private void DetectExternalActivity(string jsonlPath)
	{
		try
		{
			var lastLine = ReadLastLine(jsonlPath);
			if (lastLine == null)
				return;

			using var doc = System.Text.Json.JsonDocument.Parse(lastLine);
			var type = doc.RootElement.TryGetProperty("type", out var typeEl)
				? typeEl.GetString() : null;

			// progress, assistant (mid-stream), system with task_progress → active
			// result → done
			var isActive = type is "progress" or "assistant";

			Dispatcher.UIThread.Post(() => _node.IsExternallyActive = isActive);

			if (isActive)
			{
				_externalActivityTimeoutTimer?.Dispose();
				_externalActivityTimeoutTimer = new Timer(
					_ => Dispatcher.UIThread.Post(() => _node.IsExternallyActive = false),
					null,
					ExternalActivityTimeout,
					Timeout.InfiniteTimeSpan);
			}
			else
			{
				_externalActivityTimeoutTimer?.Dispose();
				_externalActivityTimeoutTimer = null;
			}
		}
		catch
		{
			// Best effort — don't crash on parse errors
		}
	}

	/// <summary>Reads the last non-empty line from a file without loading it all into memory.</summary>
	private static string? ReadLastLine(string path)
	{
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (stream.Length == 0)
				return null;

			// Seek backwards from end to find the last newline
			var pos = stream.Length - 1;
			// Skip trailing newlines
			while (pos > 0)
			{
				stream.Position = pos;
				var b = stream.ReadByte();
				if (b != '\n' && b != '\r')
					break;
				pos--;
			}

			// Find the start of the last line
			while (pos > 0)
			{
				stream.Position = pos - 1;
				var b = stream.ReadByte();
				if (b == '\n')
					break;
				pos--;
			}

			stream.Position = pos;
			using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
			return reader.ReadLine();
		}
		catch
		{
			return null;
		}
	}

	private void RefreshFromFile()
	{
		try
		{
			var entries = _fileService.ReadEntries(_node.FileName);

			var displayable = new List<SessionEntryModel>();
			foreach (var entry in entries)
			{
				if (entry.Role != Constants.SessionFile.RoleCompaction
				    && string.IsNullOrWhiteSpace(entry.Content))
					continue;

				displayable.Add(entry);
			}

			if (displayable.Count == 0)
				return;

			// Decide which entries are actually new inside the posted UI-thread callback, against
			// Messages.Count read at execution time rather than a snapshot taken here on the
			// timer thread — otherwise a delay before the post runs (e.g. a busy UI thread) lets
			// another in-flight update land first, making stale entries look "new" again and
			// duplicating them (see ISSUE.009).
			Dispatcher.UIThread.Post(() =>
			{
				if (displayable.Count <= Messages.Count)
					return;

				var newEntries = displayable.Skip(Messages.Count).ToList();
				_log.Information("FileWatcher: {Count} new entries detected in {FileName}",
					newEntries.Count, _node.FileName);

				foreach (var entry in newEntries)
					Messages.Add(EntryToViewModel(entry));

				// Update last prompt time if new entries include a user message
				var lastUser = newEntries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleUser);
				if (lastUser != null)
				{
					_node.LastPromptTimestamp = lastUser.Timestamp;
					_node.LastPromptTime = lastUser.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
					_node.ProfileName = lastUser.ProfileName;
				}

				var lastAssistant = newEntries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleAssistant);
				if (lastAssistant != null)
					_node.LastResponseTimestamp = lastAssistant.Timestamp;
			});
		}
		catch (Exception ex)
		{
			_log.Debug("FileWatcher: error reading {FileName}: {Error}", _node.FileName, ex.Message);
		}
	}

	private void RefreshFromJsonl(string jsonlPath)
	{
		try
		{
			// Parse the full JSONL and find entries beyond what we already have
			var allEntries = _importService.ParseJsonlSession(jsonlPath);

			// Filter to displayable entries (same logic as LoadMessagesFromFile)
			var displayable = new List<SessionEntryModel>();
			foreach (var entry in allEntries)
			{
				if (entry.Role != Constants.SessionFile.RoleCompaction
				    && string.IsNullOrWhiteSpace(entry.Content))
					continue;
				displayable.Add(entry);
			}

			if (displayable.Count <= _lastKnownEntryCount)
				return;

			var newEntries = displayable.Skip(_lastKnownEntryCount).ToList();
			_log.Information("JSONL watcher: {Count} new entries detected from JSONL", newEntries.Count);

			_lastKnownEntryCount = displayable.Count;

			Dispatcher.UIThread.Post(() =>
			{
				// Defensive dedup (ISSUE.009 follow-up): _lastKnownEntryCount is captured by
				// re-reading the JSONL file, which can race a persistent (Streaming Session Mode)
				// process's own disk flush of the entry TurnController's HandleStreamEvent just
				// wrote via the primary path. If that flush lands after this method's snapshot but
				// before the count is next refreshed, the same entry is treated as "new" here and
				// would otherwise be appended a second time (with none of HandleStreamEvent's
				// model/effort metadata) both on screen and in the persisted .txt file. Skip any
				// entry whose role/content exactly matches the current tail of Messages.
				var addedEntries = new List<SessionEntryModel>();
				foreach (var entry in newEntries)
				{
					var last = Messages.Count > 0 ? Messages[^1] : null;
					if (last != null && last.Role == entry.Role && last.Content == entry.Content)
						continue;

					Messages.Add(EntryToViewModel(entry));
					addedEntries.Add(entry);
				}

				// Append to the Tessyn .txt file only now that Messages already reflects these
				// entries. Writing before Messages.Add let the .txt FileSystemWatcher's own
				// debounced RefreshFromFile race this Dispatcher.Post — if the UI thread was busy
				// (e.g. right after an app/machine restart) RefreshFromFile could see the same
				// entries as "new" (Messages.Count not yet incremented) and re-add them, duplicating
				// both the user message and the assistant reply in the pane.
				foreach (var entry in addedEntries)
					_fileService.AppendMessage(_node.FileName, entry.Role, entry.Content);

				var lastUser = addedEntries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleUser);
				if (lastUser != null)
				{
					_node.LastPromptTimestamp = lastUser.Timestamp;
					_node.LastPromptTime = lastUser.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
					_node.ProfileName = lastUser.ProfileName;
				}

				var lastAssistant = addedEntries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleAssistant);
				if (lastAssistant != null)
					_node.LastResponseTimestamp = lastAssistant.Timestamp;
			});
		}
		catch (Exception ex)
		{
			_log.Debug("JSONL watcher: error reading {Path}: {Error}", jsonlPath, ex.Message);
		}
	}

	/// <summary>Updates the JSONL entry count baseline. Called after a send completes so the watcher doesn't re-detect entries already processed during that send.</summary>
	public void RefreshJsonlEntryCount() => _lastKnownEntryCount = InitializeJsonlEntryCount();

	/// <summary>Switches the Messages collection between TessynDesktop .txt and Claude CLI JSONL sources.</summary>
	private void SwapMessageSource(bool showClaude)
	{
		if (showClaude)
		{
			// Snapshot current Tessyn messages so we can restore them
			_tessynMessagesSnapshot = new List<MessageEntryViewModel>(Messages);

			var allPaths = GetAllJsonlPaths();
			if (allPaths.Count == 0)
			{
				_log.Warning("Cannot show Claude session: no JSONL files found");
				_isClaudeSessionView = false;
				this.RaisePropertyChanged(nameof(IsClaudeSessionView));
				return;
			}

			Messages.Clear();
			for (var i = 0; i < allPaths.Count; i++)
			{
				var (sessionId, path) = allPaths[i];

				// Add a visual separator header for each JSONL file
				var fileName = Path.GetFileName(path);
				var fileInfo = new FileInfo(path);
				var startedAt = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm");
				var status = i < allPaths.Count - 1 ? "detached" : "current";
				var separator = "═══════════════════════════════════════════════════════════════";
				Messages.Add(new MessageEntryViewModel
				{
					Role      = Constants.SessionFile.RoleSystem,
					Content   = $"{separator}\n{fileName}  ({status}, started {startedAt})\n{separator}",
					Timestamp = fileInfo.CreationTimeUtc,
				});

				var entries = _importService.ParseJsonlSessionRaw(path);
				foreach (var entry in entries)
				{
					if (entry.Role != Constants.SessionFile.RoleCompaction
					    && string.IsNullOrWhiteSpace(entry.Content))
						continue;
					Messages.Add(EntryToViewModel(entry));
				}
			}
		}
		else
		{
			// Restore the Tessyn messages
			Messages.Clear();
			if (_tessynMessagesSnapshot != null)
			{
				foreach (var msg in _tessynMessagesSnapshot)
					Messages.Add(msg);
				_tessynMessagesSnapshot = null;
			}
		}
	}

	/// <summary>Returns all JSONL paths (prior + current) in chronological order, filtered to existing files.</summary>
	private List<(string SessionId, string Path)> GetAllJsonlPaths()
	{
		if (string.IsNullOrEmpty(_node.Model.WorkingDirectory))
			return [];

		var result = new List<(string, string)>();

		foreach (var priorId in _node.Model.PriorClaudeSessionIds)
		{
			var path = BuildJsonlPathForSessionId(priorId);
			if (path != null && File.Exists(path))
				result.Add((priorId, path));
		}

		var currentId = _node.Model.ClaudeSessionId;
		if (!string.IsNullOrEmpty(currentId))
		{
			var path = BuildJsonlPathForSessionId(currentId);
			if (path != null && File.Exists(path))
				result.Add((currentId, path));
		}

		return result;
	}

	/// <summary>
	/// Locates the JSONL for <paramref name="sessionId"/> under any known Claude CLI home — the
	/// OS-default plus every profile directory (active or removed) — so this session's raw Claude
	/// Session View and file watching keep working even after its originating profile is removed
	/// (FR.12.18). Returns null if it can't be found anywhere.
	/// </summary>
	private string? BuildJsonlPathForSessionId(string sessionId)
	{
		if (string.IsNullOrEmpty(_node.Model.WorkingDirectory))
			return null;

		return Constants.ClaudeSessions.FindExistingJsonlPath(_node.Model.WorkingDirectory, sessionId);
	}

	/// <summary>Converts a persisted session entry into a display view-model. Public so TurnController can reuse it when rewriting Messages after compaction.</summary>
	public static MessageEntryViewModel EntryToViewModel(SessionEntryModel entry)
	{
		string? modelLabel = null;
		if (entry.ModelId != null || entry.Effort != null)
			modelLabel = $"[{entry.ModelId ?? "default"}, {entry.Effort ?? "default"}]";

		return new MessageEntryViewModel
		{
			Role        = entry.Role,
			Content     = entry.Content,
			Timestamp   = entry.Timestamp,
			ProfileName = entry.ProfileName,
			ModelLabel  = modelLabel,
		};
	}

	public void Dispose()
	{
		_fileWatcher?.Dispose();
		_jsonlWatcher?.Dispose();
		_fileChangeDebounceTimer?.Dispose();
		_jsonlChangeDebounceTimer?.Dispose();
		_externalActivityTimeoutTimer?.Dispose();
	}
}
