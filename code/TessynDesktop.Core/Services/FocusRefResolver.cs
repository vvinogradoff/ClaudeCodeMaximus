using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IFocusRefResolver" />
/// <remarks>Created by Claude</remarks>
public sealed class FocusRefResolver : IFocusRefResolver
{
	private readonly ISessionRegistry _registry;
	private readonly ISessionContentStore? _store;
	private readonly string _dataRoot;

	public FocusRefResolver(ISessionRegistry registry, ISessionContentStore? store = null, string? dataRoot = null)
	{
		_registry = registry;
		_store    = store;
		_dataRoot = dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.AppDataFolderName);
	}

	/// <inheritdoc />
	public AttentionRefModel Resolve(SessionNodeModel caller, FocusRefRequest request) => request.Kind switch
	{
		Constants.Attention.RefKindEntry => ResolveEntry(caller, request),
		Constants.Attention.RefKindFile  => ResolveFile(caller, request),
		Constants.Attention.RefKindUrl   => ResolveUrl(request),
		_ => throw new FocusRefException($"unknown kind '{request.Kind}' - use entry, file or url."),
	};

	// ── url ──────────────────────────────────────────────────────────────────

	private static AttentionRefModel ResolveUrl(FocusRefRequest request)
	{
		var text = request.Url?.Trim();
		if (string.IsNullOrEmpty(text))
			throw new FocusRefException("'url' is required for kind=url.");
		if (text.Length > Constants.Attention.MaxRefUrlLength)
			throw new FocusRefException($"'url' is longer than {Constants.Attention.MaxRefUrlLength} characters.");
		if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			throw new FocusRefException($"'{Shorten(text)}' is not an http or https URL.");

		return new AttentionRefModel { Kind = Constants.Attention.RefKindUrl, Label = Label(request), Url = text };
	}

	// ── file ─────────────────────────────────────────────────────────────────

	private AttentionRefModel ResolveFile(SessionNodeModel caller, FocusRefRequest request)
	{
		var raw = request.Path?.Trim();
		if (string.IsNullOrEmpty(raw))
			throw new FocusRefException("'path' is required for kind=file.");

		string full;
		try
		{
			full = Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(caller.WorkingDirectory, raw));
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			throw new FocusRefException($"'{Shorten(raw)}' is not a valid path.");
		}

		if (!File.Exists(full))
			throw new FocusRefException($"file '{Shorten(raw)}' does not exist (resolved to {full}) or is not a regular file.");

		if (FileAccessPolicy.IsDenied(full, _dataRoot))
			throw new FocusRefException($"file '{Shorten(raw)}' cannot be referenced: it is a credential, key or token file.");

		var info = new FileInfo(full);
		return new AttentionRefModel
		{
			Kind         = Constants.Attention.RefKindFile,
			Label        = Label(request),
			RefId        = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
			Name         = info.Name,
			RelativePath = RelativeToWorkingDirectory(caller.WorkingDirectory, full),
			AbsolutePath = full,
			Size         = info.Length,
			Mime         = FileAccessPolicy.Classify(full).Mime,
			MtimeUtc     = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
		};
	}

	/// <summary>Forward-slash path below <paramref name="workingDirectory"/>, or null when the file lies outside it.</summary>
	internal static string? RelativeToWorkingDirectory(string workingDirectory, string fullPath)
	{
		if (string.IsNullOrWhiteSpace(workingDirectory))
			return null;

		var root = Path.GetFullPath(workingDirectory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
			? fullPath[root.Length..].Replace('\\', '/')
			: null;
	}

	// ── entry ────────────────────────────────────────────────────────────────

	private AttentionRefModel ResolveEntry(SessionNodeModel caller, FocusRefRequest request)
	{
		var target = ResolveSession(caller, request.Session);

		var quote = request.Quote ?? string.Empty;
		if (quote.Length > Constants.Attention.MaxRefQuoteLength)
			throw new FocusRefException($"'quote' is longer than {Constants.Attention.MaxRefQuoteLength} characters - quote a shorter, distinctive part.");

		var (needle, _) = Normalize(quote);
		if (needle.Length == 0)
			throw new FocusRefException("'quote' is required for kind=entry and must not be blank.");

		var last = request.Occurrence switch
		{
			null or "" or Constants.Attention.RefOccurrenceLast => true,
			Constants.Attention.RefOccurrenceFirst              => false,
			_ => throw new FocusRefException($"'occurrence' must be {Constants.Attention.RefOccurrenceLast} or {Constants.Attention.RefOccurrenceFirst}."),
		};

		if (_store == null)
			throw new FocusRefException("entry references need the daemon's content store, which is not available.");

		var hit = Search(target, needle, last, out var searched)
			?? throw new FocusRefException(
				$"the quote \"{Shorten(quote)}\" was not found in {searched} entries of session {target.NodeId} " +
				"(whitespace is normalised, everything else must match exactly - copy the text verbatim from the entry).");

		return new AttentionRefModel
		{
			Kind    = Constants.Attention.RefKindEntry,
			Label   = Label(request),
			NodeId  = target.NodeId,
			FileId  = EntryIds.FormatFileId(hit.FileId),
			EntryId = EntryIds.Format(hit.FileId, hit.Ordinal),
			Offset  = hit.Offset,
			Length  = hit.Length,
		};
	}

	private SessionNodeModel ResolveSession(SessionNodeModel caller, string? idOrPrefix)
	{
		if (string.IsNullOrWhiteSpace(idOrPrefix))
			return caller;

		var id = idOrPrefix.Trim();
		var resolution = _registry.ResolveNodeId(id);
		switch (resolution.Status)
		{
			case NodeIdResolutionStatus.TooShort:
				throw new FocusRefException($"session id '{id}' is too short: use at least {Constants.Agent.NodeIdPrefixMinLength} characters.");
			case NodeIdResolutionStatus.Ambiguous:
				throw new FocusRefException($"session id '{id}' matches {resolution.MatchCount} sessions - use more characters.");
			case NodeIdResolutionStatus.Resolved when resolution.NodeId != null:
				return _registry.FindModelByNodeId(resolution.NodeId) ?? throw new FocusRefException($"session '{id}' was not found.");
			default:
				throw new FocusRefException($"session '{id}' was not found.");
		}
	}

	private sealed record QuoteHit(Guid FileId, int Ordinal, int Offset, int Length);

	/// <summary>
	/// Walks the entries from the newest (<paramref name="last"/>) or the oldest and stops at the first one that holds the quote. A
	/// system entry is skipped (it is the daemon's own text, such as the pinned items). That entry must hold the quote exactly once.
	/// </summary>
	private QuoteHit? Search(SessionNodeModel target, string needle, bool last, out int searched)
	{
		searched = 0;
		Guid? fileId = null;
		int? before = null;
		int? after = -1;

		while (true)
		{
			var page = _store!.ReadPage(target.FileName, new SessionContentPageRequest(
				last ? before : null, last ? null : after, Constants.Attention.RefSearchPageEntries, int.MaxValue, int.MaxValue));

			if (fileId.HasValue && fileId.Value != page.FileId)
				throw new FocusRefException("the session history changed while the quote was searched - post again.");
			fileId = page.FileId;

			if (page.Entries.Count == 0)
				return null;

			IEnumerable<SessionContentEntryModel> ordered = last ? Reverse(page.Entries) : page.Entries;
			foreach (var entry in ordered)
			{
				searched++;
				var role = entry.Entry.Role;
				if (role == Constants.SessionFile.RoleSystem || role == Constants.SessionFile.RoleCompaction)
					continue;

				var found = FindAll(entry.Entry.Content, needle);
				if (found.Count == 0)
					continue;
				if (found.Count > 1)
					throw new FocusRefException(
						$"the quote occurs {found.Count} times in one entry (entry {entry.Ordinal} of session {target.NodeId}) - quote a longer, unique part.");

				return new QuoteHit(page.FileId, entry.Ordinal, found[0].Offset, found[0].Length);
			}

			if (last)
			{
				if (!page.HasMoreBefore)
					return null;
				before = page.Entries[0].Ordinal;
			}
			else
			{
				if (!page.HasMoreAfter)
					return null;
				after = page.Entries[^1].Ordinal;
			}
		}
	}

	private static IEnumerable<SessionContentEntryModel> Reverse(IReadOnlyList<SessionContentEntryModel> entries)
	{
		for (var i = entries.Count - 1; i >= 0; i--)
			yield return entries[i];
	}

	/// <summary>Non-overlapping occurrences of <paramref name="needle"/> in <paramref name="content"/>, as offset and length in the original text.</summary>
	internal static List<(int Offset, int Length)> FindAll(string content, string needle)
	{
		var result = new List<(int, int)>();
		var (text, map) = Normalize(content);
		var at = 0;
		while (at <= text.Length - needle.Length)
		{
			var i = text.IndexOf(needle, at, StringComparison.Ordinal);
			if (i < 0)
				break;

			var start = map[i];
			var end   = map[i + needle.Length - 1] + 1;
			result.Add((start, end - start));
			at = i + needle.Length;
		}

		return result;
	}

	/// <summary>
	/// Collapses every run of whitespace to one space and trims; <c>map[i]</c> is the index in <paramref name="text"/> of the character at
	/// <c>i</c> of the result (a collapsed run maps to its first character).
	/// </summary>
	internal static (string Text, int[] Map) Normalize(string text)
	{
		var sb  = new StringBuilder(text.Length);
		var map = new List<int>(text.Length);
		var pendingSpace = -1;
		for (var i = 0; i < text.Length; i++)
		{
			if (char.IsWhiteSpace(text[i]))
			{
				if (sb.Length > 0 && pendingSpace < 0)
					pendingSpace = i;
				continue;
			}

			if (pendingSpace >= 0)
			{
				sb.Append(' ');
				map.Add(pendingSpace);
				pendingSpace = -1;
			}

			sb.Append(text[i]);
			map.Add(i);
		}

		return (sb.ToString(), [.. map]);
	}

	private static string? Label(FocusRefRequest request)
	{
		var label = request.Label?.Trim();
		if (string.IsNullOrEmpty(label))
			return null;

		return label.Length > Constants.Attention.MaxRefLabelLength ? label[..Constants.Attention.MaxRefLabelLength] : label;
	}

	private static string Shorten(string text) => text.Length > 80 ? text[..80] + "..." : text;
}
