using System;
using System.Collections.Generic;
using System.Text;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Finds the duplicate USER/ASSISTANT entries the JSONL write-back left in a session .txt file (the CRLF-versus-LF identity miss of
/// <see cref="JsonlWriteBackPlanner"/>) and removes them from the raw file text. Pure: it never touches the disk; the caller decides what to
/// do with <see cref="SessionCleanupResult.CleanedText"/>.
/// </summary>
/// <remarks>
/// Deliberately narrow, so a legitimate repeat survives: the entry is removed only when the previous entry has the same role (USER or
/// ASSISTANT) and the same multi-line content, was written at most <see cref="MaxGapSeconds"/> earlier, and the removed copy has no header
/// metadata (the write-back appends without profile, model, effort or source). Single-line entries were always matched by the old planner, so
/// a single-line repeat (a model answering two notifications with the same sentence) is never touched. Created by Claude.
/// </remarks>
public static class SessionDuplicateCleaner
{
	/// <summary>The largest gap between the original and the write-back copy; the measured gaps are 1 s (typically) up to a few seconds.</summary>
	public const int MaxGapSeconds = 10;

	/// <summary>Analyses <paramref name="rawText"/> and returns the text without the duplicate entries, plus what was removed.</summary>
	public static SessionCleanupResult Clean(string rawText)
	{
		var lines = rawText.Split('\n');
		var blocks = new List<Block>();

		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i].TrimEnd('\r');
			if (!SessionFileService.TryParseHeader(line, out var timestamp, out var role, out var rawMeta))
				continue;

			if (blocks.Count > 0)
				blocks[^1].End = i;

			blocks.Add(new Block { Start = i, End = lines.Length, Timestamp = timestamp, Role = role, HasMeta = !string.IsNullOrWhiteSpace(rawMeta) });
		}

		var removed = new List<RemovedDuplicate>();
		var drop = new HashSet<int>();
		Block? previous = null;

		foreach (var block in blocks)
		{
			var content = ContentOf(lines, block);
			block.Identity = JsonlWriteBackPlanner.IdentityOf(content);

			if (previous != null && IsWriteBackCopy(previous, block))
			{
				for (var i = block.Start; i < block.End; i++)
					drop.Add(i);
				removed.Add(new RemovedDuplicate(block.Role, block.Timestamp, block.Identity.Length, Excerpt(block.Identity)));
				continue; // the kept original stays the comparison point, so a triple is reduced to one
			}

			previous = block;
		}

		if (removed.Count == 0)
			return new SessionCleanupResult(rawText, removed);

		var sb = new StringBuilder(rawText.Length);
		for (var i = 0; i < lines.Length; i++)
		{
			if (drop.Contains(i))
				continue;

			sb.Append(lines[i]);
			if (i < lines.Length - 1)
				sb.Append('\n');
		}

		return new SessionCleanupResult(sb.ToString(), removed);
	}

	private static bool IsWriteBackCopy(Block original, Block copy) =>
		copy.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant
		&& copy.Role == original.Role
		&& !copy.HasMeta
		&& copy.Identity.Contains('\n')
		&& copy.Identity == original.Identity
		&& (copy.Timestamp - original.Timestamp).TotalSeconds is >= 0 and <= MaxGapSeconds;

	private static string ContentOf(string[] lines, Block block)
	{
		var end = block.End;
		while (end > block.Start + 1 && string.IsNullOrWhiteSpace(lines[end - 1]))
			end--;

		return string.Join('\n', lines[(block.Start + 1)..end]);
	}

	private static string Excerpt(string identity)
	{
		var flat = identity.Replace('\n', ' ');
		return flat.Length <= 60 ? flat : flat[..60] + "...";
	}

	private sealed class Block
	{
		public int Start;
		public int End;
		public DateTimeOffset Timestamp;
		public string Role = string.Empty;
		public bool HasMeta;
		public string Identity = string.Empty;
	}
}

/// <summary>One entry <see cref="SessionDuplicateCleaner"/> removed.</summary>
public sealed record RemovedDuplicate(string Role, DateTimeOffset Timestamp, int Length, string Excerpt);

/// <summary>The outcome of cleaning one session file's text.</summary>
public sealed record SessionCleanupResult(string CleanedText, IReadOnlyList<RemovedDuplicate> Removed);
