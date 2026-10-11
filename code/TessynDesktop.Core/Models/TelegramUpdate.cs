using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>
/// A single parsed Telegram <c>getUpdates</c> update, reduced to the fields the Telegram Bridge
/// needs (FR.21.4). One instance is produced per raw update (FR.21.12): updates the bridge cannot
/// act on (non-message, edited, unsupported message types, no sender) are returned as
/// <see cref="Ignored"/> markers with <see cref="IsActionable"/> false so their ids still advance
/// the persisted offset. <see cref="Text"/> is the message <c>text</c> or, for media, its
/// <c>caption</c> (FR.21.13).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record TelegramUpdate(
	long UpdateId,
	long ChatId,
	long FromUserId,
	string Text,
	int? MessageThreadId,
	IReadOnlyList<TelegramAttachment>? Attachments = null,
	string? MediaGroupId = null,
	bool IsActionable = true)
{
	public bool HasAttachments => Attachments is { Count: > 0 };

	/// <summary>A marker for an update that must be acknowledged but not acted on (FR.21.12).</summary>
	public static TelegramUpdate Ignored(long updateId) =>
		new(updateId, ChatId: 0, FromUserId: 0, Text: string.Empty, MessageThreadId: null, IsActionable: false);
}

/// <summary>The kinds of Telegram media the bridge downloads (FR.21.13).</summary>
public enum TelegramAttachmentKind
{
	Photo,
	Document,
	Video,
	Audio,
	Voice,
	VideoNote,
	Animation,
}

/// <summary>One downloadable file reference carried by an inbound message (FR.21.13).</summary>
public sealed record TelegramAttachment(
	TelegramAttachmentKind Kind,
	string FileId,
	string FileUniqueId,
	string? FileName,
	string? MimeType,
	long? FileSize);

/// <summary>Result of the Bot API <c>getFile</c> call: the server-side path to download from.</summary>
public sealed record TelegramFileInfo(string FilePath, long? FileSize);
