using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>autocomplete.suggest</c> for a local client (package J2): the <c>#</c> symbol, <c>##</c> file and drive-path suggestions of the composer,
/// built with the same <see cref="AutocompleteTriggerParser"/> and <see cref="ICodeIndexService"/> as the Avalonia popup. Loopback only. The
/// debounce stays on the client. A code index that is not built yet is never waited for (ISSUE.016): see <see cref="AutocompleteIndexLeases"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AutocompleteRpcModule : IRemoteRpcModule, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<AutocompleteRpcModule>();

	private static readonly char[] PathSeparators = ['\\', '/'];

	private readonly ISessionRegistry _registry;
	private readonly ICodeIndexService _index;
	private readonly AutocompleteIndexLeases _leases;
	private readonly AutocompleteTriggerParser _parser = new();

	public AutocompleteRpcModule(ISessionRegistry registry, ICodeIndexService index)
	{
		_registry = registry;
		_index    = index;
		_leases   = new AutocompleteIndexLeases(index);
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.Add(RemoteProtocol.Composer.AutocompleteSuggestMethod, (p, _) => SuggestAsync(p), remoteCallable: false);

	public void Dispose() => _leases.Dispose();

	private async Task<object?> SuggestAsync(JsonNode? @params)
	{
		var node  = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var text  = TdcpParams.RequireString(@params, "text");
		var caret = TdcpParams.OptionalInt(@params, "caret") ?? throw new TdcpInvalidParamsException("'caret' is required");

		var trigger = _parser.Parse(text, caret);
		if (trigger.Mode == AutocompleteMode.None || string.IsNullOrEmpty(trigger.Query))
			return new AutocompleteSuggestResult(RemoteProtocol.Composer.AutocompleteModes.None, 0, 0, true, []);

		var directory = node.WorkingDirectory;

		// Off the request path: only a lookup in a ready index or a bounded directory listing happens here; a missing index is built in the background.
		return await Task.Run(() => Build(trigger, directory));
	}

	private AutocompleteSuggestResult Build(AutocompleteTriggerModel trigger, string workingDirectory)
	{
		string mode;
		IReadOnlyList<AutocompleteItemDto> items;
		var ready = true;

		switch (trigger.Mode)
		{
			case AutocompleteMode.File:
				mode  = RemoteProtocol.Composer.AutocompleteModes.File;
				ready = _leases.Touch(workingDirectory);
				items = ready ? FileItems(workingDirectory, trigger.Query) : [];
				break;

			case AutocompleteMode.Symbol:
				mode  = RemoteProtocol.Composer.AutocompleteModes.Symbol;
				ready = _leases.Touch(workingDirectory);
				items = ready ? SymbolItems(workingDirectory, trigger.Query) : [];
				break;

			default:
				mode  = RemoteProtocol.Composer.AutocompleteModes.Path;
				items = PathItems(trigger.Query);
				break;
		}

		return new AutocompleteSuggestResult(mode, trigger.TriggerStartIndex, trigger.TriggerLength, ready, items);
	}

	private IReadOnlyList<AutocompleteItemDto> FileItems(string workingDirectory, string query) =>
		_index.SearchFiles(workingDirectory, query, Constants.CodeIndex.MaxSuggestions)
			.Select(file => new AutocompleteItemDto(
				file.FileName, file.RelativePath, Constants.Composer.FileKind, file.RelativePath, MatchIndex(file.FileName, query), query.Length))
			.ToList();

	private IReadOnlyList<AutocompleteItemDto> SymbolItems(string workingDirectory, string query) =>
		_index.SearchSymbols(workingDirectory, query, Constants.CodeIndex.MaxSuggestions)
			.Select(symbol => new AutocompleteItemDto(
				symbol.FullyQualifiedName,
				string.IsNullOrEmpty(symbol.Namespace) ? symbol.FullyQualifiedName : symbol.Namespace + "." + symbol.FullyQualifiedName,
				symbol.Kind.ToString().ToLowerInvariant(),
				string.IsNullOrEmpty(symbol.Namespace) ? null : symbol.Namespace,
				MatchIndex(symbol.FullyQualifiedName, query),
				query.Length))
			.ToList();

	/// <summary>Drive paths: directories first, then files, filtered by the typed name prefix; unreadable or missing directories give nothing.</summary>
	private IReadOnlyList<AutocompleteItemDto> PathItems(string pathQuery)
	{
		var items = new List<AutocompleteItemDto>();
		var lastSeparator = pathQuery.LastIndexOfAny(PathSeparators);
		if (lastSeparator < 0)
			return items;

		var directory  = pathQuery[..(lastSeparator + 1)];
		var nameFilter = pathQuery[(lastSeparator + 1)..];

		try
		{
			if (!Directory.Exists(directory))
				return items;

			foreach (var path in Directory.EnumerateDirectories(directory))
			{
				if (items.Count >= Constants.CodeIndex.MaxSuggestions)
					break;

				var name = Path.GetFileName(path);
				if (name.StartsWith(nameFilter, StringComparison.OrdinalIgnoreCase))
					items.Add(new AutocompleteItemDto(name, path + "\\", Constants.Composer.DirectoryKind, null, 0, nameFilter.Length));
			}

			foreach (var path in Directory.EnumerateFiles(directory))
			{
				if (items.Count >= Constants.CodeIndex.MaxSuggestions)
					break;

				var name = Path.GetFileName(path);
				if (name.StartsWith(nameFilter, StringComparison.OrdinalIgnoreCase))
					items.Add(new AutocompleteItemDto(name, path, Constants.Composer.FileKind, Path.GetExtension(path), 0, nameFilter.Length));
			}
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
		{
			_log.Debug(ex, "Autocomplete could not list {Path}", pathQuery);
		}

		return items;
	}

	private static int MatchIndex(string text, string query)
	{
		var index = text.IndexOf(query, StringComparison.Ordinal);
		if (index >= 0)
			return index;

		index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
		return index >= 0 ? index : 0;
	}
}
