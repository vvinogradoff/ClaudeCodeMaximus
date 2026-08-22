namespace TessynDesktop.Models;

/// <summary>
/// A single parsed Telegram <c>getUpdates</c> update, reduced to the fields the Telegram Bridge
/// needs (FR.21.4). Non-message updates and messages without text are not represented — callers
/// filter for those before this type is constructed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record TelegramUpdate(
	long UpdateId,
	long ChatId,
	long FromUserId,
	string Text,
	int? MessageThreadId);
