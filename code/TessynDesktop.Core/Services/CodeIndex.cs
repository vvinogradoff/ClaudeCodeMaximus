using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>
/// Per-working-directory file and symbol index. Built once, then kept current incrementally: a file event touches only
/// that file (or that directory's subtree), never the whole tree. A full re-scan used to run after every change, which on
/// a 300k-file tree meant seconds of CPU and hundreds of MB of garbage per change burst while the app sat idle.
/// </remarks>
public sealed class CodeIndex : IDisposable
{
	private static readonly ILogger _log = Log.ForContext<CodeIndex>();

	private static readonly HashSet<string> _symbolParsedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".cs"
	};

	private static readonly HashSet<string> _excludedDirectories = new(Constants.CodeIndex.ExcludedDirectories, StringComparer.OrdinalIgnoreCase);

	private static readonly EnumerationOptions _enumerationOptions = new()
	{
		IgnoreInaccessible = true,
		RecurseSubdirectories = false,
		// Junctions and symlinks would duplicate (or cycle through) trees that are indexed elsewhere.
		AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
	};

	private readonly string _workingDirectory;
	private readonly bool _enableWatcher;
	private readonly CancellationTokenSource _cts = new();
	private FileSystemWatcher? _watcher;
	private Timer? _debounceTimer;
	private readonly HashSet<string> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _pendingLock = new();
	private bool _fullRescanPending;
	private int _processing;

	// Index data, guarded by _dataLock. Snapshots are rebuilt lazily so a burst of changes costs one array copy at the
	// next search, not one per change.
	private readonly object _dataLock = new();
	private Dictionary<string, IndexedFileModel> _files = new(StringComparer.OrdinalIgnoreCase);
	private Dictionary<string, CodeSymbolModel[]> _symbolsByFile = new(StringComparer.OrdinalIgnoreCase);
	private IndexedFileModel[]? _fileArray = Array.Empty<IndexedFileModel>();
	private CodeSymbolModel[]? _symbolArray = Array.Empty<CodeSymbolModel>();
	private volatile bool _isReady;
	private bool _truncated;

	public int RefCount;

	public bool IsReady => _isReady;

	public IndexedFileModel[] FileSnapshot
	{
		get { lock (_dataLock) return _fileArray ??= _files.Values.ToArray(); }
	}

	public CodeSymbolModel[] SymbolSnapshot
	{
		get { lock (_dataLock) return _symbolArray ??= _symbolsByFile.Values.SelectMany(static x => x).ToArray(); }
	}

	public CodeIndex(string workingDirectory, bool enableWatcher = true)
	{
		_workingDirectory = workingDirectory;
		_enableWatcher = enableWatcher;
	}

	public async Task BuildAsync()
	{
		await Task.Run(() => FullScan(), _cts.Token);
		if (_enableWatcher)
			StartWatcher();
		_isReady = true;
		_log.Information("Code index ready for {Directory}: {FileCount} files, {SymbolCount} symbols",
			_workingDirectory, FileSnapshot.Length, SymbolSnapshot.Length);
	}

	private void FullScan()
	{
		var files = new Dictionary<string, IndexedFileModel>(StringComparer.OrdinalIgnoreCase);
		var symbols = new Dictionary<string, CodeSymbolModel[]>(StringComparer.OrdinalIgnoreCase);

		var truncated = !ScanDirectory(_workingDirectory, files, symbols);

		lock (_dataLock)
		{
			_files = files;
			_symbolsByFile = symbols;
			_fileArray = null;
			_symbolArray = null;
			_truncated = truncated;
		}

		if (truncated)
			_log.Warning("Code index for {Directory} truncated at {Max} files", _workingDirectory, Constants.CodeIndex.MaxIndexedFiles);
	}

	/// <summary>Adds the subtree under <paramref name="directory"/> to the maps. Returns false once the file cap is reached.</summary>
	private bool ScanDirectory(string directory, Dictionary<string, IndexedFileModel> files, Dictionary<string, CodeSymbolModel[]> symbols)
	{
		if (_cts.Token.IsCancellationRequested) return true;

		try
		{
			foreach (var filePath in Directory.EnumerateFiles(directory, "*", _enumerationOptions))
			{
				if (files.Count >= Constants.CodeIndex.MaxIndexedFiles)
					return false;

				AddFile(filePath, files, symbols);
			}

			foreach (var subDir in Directory.EnumerateDirectories(directory, "*", _enumerationOptions))
			{
				if (_excludedDirectories.Contains(Path.GetFileName(subDir))) continue;
				if (!ScanDirectory(subDir, files, symbols)) return false;
			}
		}
		catch (UnauthorizedAccessException)
		{
			// Skip directories we can't access
		}
		catch (DirectoryNotFoundException)
		{
			// Directory may have been deleted during scan
		}

		return true;
	}

	/// <summary>The key an entry is stored under: the root-relative, '/'-separated path. The model's RelativePath is the same string instance, so a file costs one path string, not two.</summary>
	private string ToKey(string absolutePath) => Path.GetRelativePath(_workingDirectory, absolutePath).Replace('\\', '/');

	private void AddFile(string filePath, Dictionary<string, IndexedFileModel> files, Dictionary<string, CodeSymbolModel[]> symbols)
	{
		var key = ToKey(filePath);
		files[key] = new IndexedFileModel
		{
			FileName = Path.GetFileName(filePath),
			RelativePath = key,
			Root = _workingDirectory,
		};

		if (_symbolParsedExtensions.Contains(Path.GetExtension(filePath)))
		{
			var list = new List<CodeSymbolModel>();
			ExtractSymbols(filePath, list);
			if (list.Count > 0)
				symbols[key] = list.ToArray();
			else
				symbols.Remove(key);
		}
	}

	/// <summary>
	/// Applies file-system changes to the index: a path that is now a file is (re)indexed, a path that is now a directory has
	/// its subtree re-scanned, and a path that no longer exists is removed (with everything below it, in case it was a directory).
	/// </summary>
	internal void ApplyChanges(IReadOnlyCollection<string> paths)
	{
		foreach (var path in paths)
		{
			if (_cts.Token.IsCancellationRequested) return;
			if (IsExcluded(path)) continue;

			try
			{
				var entryKey = ToKey(path);
				if (File.Exists(path))
				{
					lock (_dataLock)
					{
						if (_truncated && !_files.ContainsKey(entryKey) && _files.Count >= Constants.CodeIndex.MaxIndexedFiles) continue;
					}

					var one = new Dictionary<string, IndexedFileModel>(1);
					var oneSymbols = new Dictionary<string, CodeSymbolModel[]>(1);
					AddFile(path, one, oneSymbols);
					lock (_dataLock)
					{
						_files[entryKey] = one[entryKey];
						if (oneSymbols.TryGetValue(entryKey, out var symbols))
							_symbolsByFile[entryKey] = symbols;
						else
							_symbolsByFile.Remove(entryKey);
						_fileArray = null;
						_symbolArray = null;
					}
				}
				else if (Directory.Exists(path))
				{
					RemoveSubtree(entryKey);
					var files = new Dictionary<string, IndexedFileModel>();
					var symbols = new Dictionary<string, CodeSymbolModel[]>();
					ScanDirectory(path, files, symbols);
					lock (_dataLock)
					{
						foreach (var (key, value) in files)
						{
							if (_files.Count >= Constants.CodeIndex.MaxIndexedFiles) break;
							_files[key] = value;
						}

						foreach (var (key, value) in symbols)
							_symbolsByFile[key] = value;
						_fileArray = null;
						_symbolArray = null;
					}
				}
				else
				{
					RemoveSubtree(entryKey);
				}
			}
			catch (Exception ex)
			{
				_log.Debug(ex, "Code index could not apply change for {Path}", path);
			}
		}
	}

	/// <summary>Removes the entry under <paramref name="entryKey"/> and, if it was a directory, every entry below it.</summary>
	private void RemoveSubtree(string entryKey)
	{
		lock (_dataLock)
		{
			var changed = _files.Remove(entryKey);
			changed |= _symbolsByFile.Remove(entryKey);

			var prefix = entryKey.TrimEnd('/') + "/";
			List<string>? doomed = null;
			foreach (var key in _files.Keys)
			{
				if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					(doomed ??= new List<string>()).Add(key);
			}

			if (doomed != null)
			{
				foreach (var key in doomed)
				{
					_files.Remove(key);
					_symbolsByFile.Remove(key);
				}

				changed = true;
			}

			if (changed)
			{
				_fileArray = null;
				_symbolArray = null;
			}
		}
	}

	/// <summary>True when any directory segment of <paramref name="filePath"/> below the root is an excluded directory.</summary>
	private bool IsExcluded(string filePath)
	{
		if (!filePath.StartsWith(_workingDirectory, StringComparison.OrdinalIgnoreCase)) return true;

		var lookup = _excludedDirectories.GetAlternateLookup<ReadOnlySpan<char>>();
		var relative = filePath.AsSpan(_workingDirectory.Length);
		while (!relative.IsEmpty)
		{
			var sep = relative.IndexOfAny('\\', '/');
			if (sep < 0) return false; // last segment is the file itself
			if (sep > 0 && lookup.Contains(relative[..sep])) return true;
			relative = relative[(sep + 1)..];
		}

		return false;
	}

	private void ExtractSymbols(string filePath, List<CodeSymbolModel> symbols)
	{
		try
		{
			if (new FileInfo(filePath).Length > Constants.CodeIndex.MaxSymbolParseBytes) return;

			var sourceText = File.ReadAllText(filePath);
			var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);
			var root = tree.GetRoot();

			ExtractSymbolsFromNode(root, filePath, symbols, namespaceParts: "", parentTypeParts: "");
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "Failed to parse {FilePath} for symbols", filePath);
		}
	}

	private void ExtractSymbolsFromNode(
		SyntaxNode node,
		string filePath,
		List<CodeSymbolModel> symbols,
		string namespaceParts,
		string parentTypeParts)
	{
		foreach (var child in node.ChildNodes())
		{
			switch (child)
			{
				case BaseNamespaceDeclarationSyntax ns:
					var nsName = string.IsNullOrEmpty(namespaceParts)
						? ns.Name.ToString()
						: namespaceParts + "." + ns.Name.ToString();
					ExtractSymbolsFromNode(child, filePath, symbols, nsName, parentTypeParts);
					break;

				case ClassDeclarationSyntax cls:
					AddTypeAndMembers(cls, cls.Identifier, CodeSymbolKind.Class,
						filePath, symbols, namespaceParts, parentTypeParts);
					break;

				case EnumDeclarationSyntax enm:
					AddSymbol(enm.Identifier.Text, CodeSymbolKind.Enum,
						filePath, enm.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
						symbols, namespaceParts, parentTypeParts);
					break;

				case StructDeclarationSyntax str:
					AddTypeAndMembers(str, str.Identifier, CodeSymbolKind.Struct,
						filePath, symbols, namespaceParts, parentTypeParts);
					break;

				case RecordDeclarationSyntax rec:
					AddTypeAndMembers(rec, rec.Identifier, CodeSymbolKind.Record,
						filePath, symbols, namespaceParts, parentTypeParts);
					break;

				case InterfaceDeclarationSyntax ifc:
					AddTypeAndMembers(ifc, ifc.Identifier, CodeSymbolKind.Interface,
						filePath, symbols, namespaceParts, parentTypeParts);
					break;
			}
		}
	}

	private void AddTypeAndMembers(
		TypeDeclarationSyntax typeDecl,
		SyntaxToken identifier,
		CodeSymbolKind kind,
		string filePath,
		List<CodeSymbolModel> symbols,
		string namespaceParts,
		string parentTypeParts)
	{
		var typeName = identifier.Text;
		var line = identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

		AddSymbol(typeName, kind, filePath, line, symbols, namespaceParts, parentTypeParts);

		var newParentParts = string.IsNullOrEmpty(parentTypeParts)
			? typeName
			: parentTypeParts + "." + typeName;

		foreach (var member in typeDecl.Members)
		{
			switch (member)
			{
				case MethodDeclarationSyntax method:
					AddSymbol(method.Identifier.Text, CodeSymbolKind.Method,
						filePath, method.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
						symbols, namespaceParts, newParentParts);
					break;

				case PropertyDeclarationSyntax prop:
					AddSymbol(prop.Identifier.Text, CodeSymbolKind.Property,
						filePath, prop.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
						symbols, namespaceParts, newParentParts);
					break;

				case ClassDeclarationSyntax nested:
					AddTypeAndMembers(nested, nested.Identifier, CodeSymbolKind.Class,
						filePath, symbols, namespaceParts, newParentParts);
					break;

				case StructDeclarationSyntax nested:
					AddTypeAndMembers(nested, nested.Identifier, CodeSymbolKind.Struct,
						filePath, symbols, namespaceParts, newParentParts);
					break;

				case RecordDeclarationSyntax nested:
					AddTypeAndMembers(nested, nested.Identifier, CodeSymbolKind.Record,
						filePath, symbols, namespaceParts, newParentParts);
					break;

				case InterfaceDeclarationSyntax nested:
					AddTypeAndMembers(nested, nested.Identifier, CodeSymbolKind.Interface,
						filePath, symbols, namespaceParts, newParentParts);
					break;

				case EnumDeclarationSyntax nested:
					AddSymbol(nested.Identifier.Text, CodeSymbolKind.Enum,
						filePath, nested.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
						symbols, namespaceParts, newParentParts);
					break;
			}
		}
	}

	private void AddSymbol(
		string name,
		CodeSymbolKind kind,
		string filePath,
		int line,
		List<CodeSymbolModel> symbols,
		string namespaceParts,
		string parentTypeParts)
	{
		var fqn = string.IsNullOrEmpty(parentTypeParts)
			? name
			: parentTypeParts + "." + name;

		symbols.Add(new CodeSymbolModel
		{
			Name = name,
			FullyQualifiedName = fqn,
			Namespace = namespaceParts,
			Kind = kind,
			FilePath = filePath,
			Line = line
		});
	}

	private void StartWatcher()
	{
		try
		{
			_watcher = new FileSystemWatcher(_workingDirectory)
			{
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
				InternalBufferSize = 64 * 1024,
			};

			_watcher.Created += OnFileEvent;
			_watcher.Changed += OnFileEvent;
			_watcher.Deleted += OnFileEvent;
			_watcher.Renamed += OnRenamedEvent;
			_watcher.Error += OnWatcherError;
			_watcher.EnableRaisingEvents = true;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Failed to start FileSystemWatcher for {Directory}", _workingDirectory);
		}
	}

	private void OnFileEvent(object sender, FileSystemEventArgs e)
	{
		// A content change to a file that is already indexed matters only for symbol-parsed files; log files and the like
		// can change thousands of times without the index needing to know.
		if (e.ChangeType == WatcherChangeTypes.Changed && !_symbolParsedExtensions.Contains(Path.GetExtension(e.FullPath)))
			return;

		ScheduleReindex(e.FullPath);
	}

	private void OnRenamedEvent(object sender, RenamedEventArgs e)
	{
		ScheduleReindex(e.OldFullPath);
		ScheduleReindex(e.FullPath);
	}

	/// <summary>The OS event buffer overflowed, so individual changes were lost: fall back to one full re-scan.</summary>
	private void OnWatcherError(object sender, ErrorEventArgs e)
	{
		_log.Warning(e.GetException(), "Code index watcher error for {Directory}; scheduling a full re-scan", _workingDirectory);
		lock (_pendingLock)
		{
			if (_cts.IsCancellationRequested) return;
			_fullRescanPending = true;
			ArmTimer();
		}
	}

	private void ScheduleReindex(string filePath)
	{
		if (IsExcluded(filePath)) return;

		lock (_pendingLock)
		{
			if (_cts.IsCancellationRequested) return;
			_pendingChanges.Add(filePath);
			ArmTimer();
		}
	}

	/// <summary>Starts or re-arms the single debounce timer. Caller holds <c>_pendingLock</c>.</summary>
	private void ArmTimer()
	{
		if (_debounceTimer == null)
			_debounceTimer = new Timer(_ => ProcessPendingChanges(), null, Constants.CodeIndex.DebounceMilliseconds, Timeout.Infinite);
		else
			_debounceTimer.Change(Constants.CodeIndex.DebounceMilliseconds, Timeout.Infinite);
	}

	private void ProcessPendingChanges()
	{
		if (_cts.Token.IsCancellationRequested) return;

		// One processor at a time; changes that arrive meanwhile re-arm the timer and are picked up afterwards.
		if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0)
		{
			lock (_pendingLock)
			{
				if (!_cts.IsCancellationRequested) ArmTimer();
			}

			return;
		}

		try
		{
			string[] changes;
			bool fullRescan;
			lock (_pendingLock)
			{
				changes = _pendingChanges.ToArray();
				_pendingChanges.Clear();
				fullRescan = _fullRescanPending;
				_fullRescanPending = false;
			}

			if (fullRescan)
				FullScan();
			else
				ApplyChanges(changes);

			_log.Debug("Code index updated after {Count} file changes (full rescan: {Full})", changes.Length, fullRescan);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Failed to update code index for {Directory}", _workingDirectory);
		}
		finally
		{
			Interlocked.Exchange(ref _processing, 0);
		}
	}

	public void Dispose()
	{
		lock (_pendingLock)
		{
			_cts.Cancel();
			_pendingChanges.Clear();
			_debounceTimer?.Dispose();
		}

		_watcher?.Dispose();
		lock (_dataLock)
		{
			_files = new Dictionary<string, IndexedFileModel>();
			_symbolsByFile = new Dictionary<string, CodeSymbolModel[]>();
			_fileArray = Array.Empty<IndexedFileModel>();
			_symbolArray = Array.Empty<CodeSymbolModel>();
		}

		_log.Debug("Code index disposed for {Directory}", _workingDirectory);
	}
}
