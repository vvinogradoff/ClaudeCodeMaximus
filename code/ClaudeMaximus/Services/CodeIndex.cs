using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class CodeIndex : IDisposable
{
	private static readonly ILogger _log = Log.ForContext<CodeIndex>();

	private static readonly HashSet<string> _indexedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".cs", ".axaml", ".xaml", ".csproj", ".sln", ".json", ".md", ".xml", ".razor"
	};

	private static readonly HashSet<string> _excludedDirectories = new(StringComparer.OrdinalIgnoreCase)
	{
		"bin", "obj", ".git", "node_modules", ".vs", ".idea"
	};

	private readonly string _workingDirectory;
	private readonly CancellationTokenSource _cts = new();
	private FileSystemWatcher? _watcher;
	private Timer? _debounceTimer;
	private readonly HashSet<string> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _pendingLock = new();

	private volatile IndexedFileModel[] _fileSnapshot = Array.Empty<IndexedFileModel>();
	private volatile CodeSymbolModel[] _symbolSnapshot = Array.Empty<CodeSymbolModel>();
	private volatile bool _isReady;

	public int RefCount;

	public bool IsReady => _isReady;
	public IndexedFileModel[] FileSnapshot => _fileSnapshot;
	public CodeSymbolModel[] SymbolSnapshot => _symbolSnapshot;

	public CodeIndex(string workingDirectory)
	{
		_workingDirectory = workingDirectory;
	}

	public async Task BuildAsync()
	{
		await Task.Run(() => FullScan(), _cts.Token);
		StartWatcher();
		_isReady = true;
		_log.Information("Code index ready for {Directory}: {FileCount} files, {SymbolCount} symbols",
			_workingDirectory, _fileSnapshot.Length, _symbolSnapshot.Length);
	}

	private void FullScan()
	{
		var files = new List<IndexedFileModel>();
		var symbols = new List<CodeSymbolModel>();

		ScanDirectory(_workingDirectory, files, symbols);

		Interlocked.Exchange(ref _fileSnapshot, files.ToArray());
		Interlocked.Exchange(ref _symbolSnapshot, symbols.ToArray());
	}

	private void ScanDirectory(string directory, List<IndexedFileModel> files, List<CodeSymbolModel> symbols)
	{
		if (_cts.Token.IsCancellationRequested) return;

		try
		{
			foreach (var subDir in Directory.EnumerateDirectories(directory))
			{
				var dirName = Path.GetFileName(subDir);
				if (_excludedDirectories.Contains(dirName)) continue;
				ScanDirectory(subDir, files, symbols);
			}

			foreach (var filePath in Directory.EnumerateFiles(directory))
			{
				var ext = Path.GetExtension(filePath);
				if (!_indexedExtensions.Contains(ext)) continue;

				var fileName = Path.GetFileName(filePath);
				var relativePath = Path.GetRelativePath(_workingDirectory, filePath).Replace('\\', '/');

				files.Add(new IndexedFileModel
				{
					FileName = fileName,
					RelativePath = relativePath,
					AbsolutePath = filePath
				});

				if (string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase))
				{
					ExtractSymbols(filePath, symbols);
				}
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
	}

	private void ExtractSymbols(string filePath, List<CodeSymbolModel> symbols)
	{
		try
		{
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
				EnableRaisingEvents = true
			};

			_watcher.Created += OnFileEvent;
			_watcher.Changed += OnFileEvent;
			_watcher.Deleted += OnFileEvent;
			_watcher.Renamed += OnRenamedEvent;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Failed to start FileSystemWatcher for {Directory}", _workingDirectory);
		}
	}

	private void OnFileEvent(object sender, FileSystemEventArgs e)
	{
		ScheduleReindex(e.FullPath);
	}

	private void OnRenamedEvent(object sender, RenamedEventArgs e)
	{
		ScheduleReindex(e.OldFullPath);
		ScheduleReindex(e.FullPath);
	}

	private void ScheduleReindex(string filePath)
	{
		var ext = Path.GetExtension(filePath);
		if (!_indexedExtensions.Contains(ext)) return;

		// Check if file is inside an excluded directory
		var relativePath = Path.GetRelativePath(_workingDirectory, filePath);
		var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (parts.Any(p => _excludedDirectories.Contains(p))) return;

		lock (_pendingLock)
		{
			_pendingChanges.Add(filePath);
		}

		_debounceTimer?.Dispose();
		_debounceTimer = new Timer(
			_ => ProcessPendingChanges(),
			null,
			Constants.CodeIndex.DebounceMilliseconds,
			Timeout.Infinite);
	}

	private void ProcessPendingChanges()
	{
		if (_cts.Token.IsCancellationRequested) return;

		HashSet<string> changes;
		lock (_pendingLock)
		{
			changes = new HashSet<string>(_pendingChanges, StringComparer.OrdinalIgnoreCase);
			_pendingChanges.Clear();
		}

		Task.Run(() =>
		{
			try
			{
				// Rebuild full index — simpler and more reliable than incremental
				FullScan();
				_log.Debug("Code index rebuilt after {Count} file changes", changes.Count);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "Failed to rebuild code index for {Directory}", _workingDirectory);
			}
		}, _cts.Token);
	}

	public void Dispose()
	{
		_cts.Cancel();
		_watcher?.Dispose();
		_debounceTimer?.Dispose();
		_cts.Dispose();
		_fileSnapshot = Array.Empty<IndexedFileModel>();
		_symbolSnapshot = Array.Empty<CodeSymbolModel>();
		_log.Debug("Code index disposed for {Directory}", _workingDirectory);
	}
}
