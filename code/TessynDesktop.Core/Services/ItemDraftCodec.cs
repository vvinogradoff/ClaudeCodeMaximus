using System;
using System.Collections.Generic;
using System.Text.Json;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The content of a Focus item's reply draft file (FR.23.19, FR.23.37). Without question values it is the note text itself, exactly as before; with
/// them (or when a note happens to start with <see cref="Constants.Composer.ItemDraftEnvelopePrefix"/>) it is that prefix, a line break and a JSON
/// object <c>{ text, questions }</c>. One file per (session, item) so listing, timestamps and cleanup stay as they were.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class ItemDraftCodec
{
	/// <summary>The file content for <paramref name="text"/> and <paramref name="questions"/>; empty when both are empty (no draft).</summary>
	public static string Encode(string text, IReadOnlyList<AttentionQuestionAnswerParams>? questions)
	{
		var hasQuestions = questions is { Count: > 0 };
		if (!hasQuestions && !text.StartsWith(Constants.Composer.ItemDraftEnvelopePrefix, StringComparison.Ordinal))
			return text;

		var envelope = new Envelope { Text = text, Questions = hasQuestions ? questions : null };
		return Constants.Composer.ItemDraftEnvelopePrefix + "\n" + JsonSerializer.Serialize(envelope, TdcpJsonOptions.Default);
	}

	/// <summary>Splits file content into the note and the question values; content that is not an envelope is all note.</summary>
	public static ItemDraftModel Decode(string? content)
	{
		if (string.IsNullOrEmpty(content))
			return new ItemDraftModel(string.Empty, null);

		if (!content.StartsWith(Constants.Composer.ItemDraftEnvelopePrefix + "\n", StringComparison.Ordinal))
			return new ItemDraftModel(content, null);

		try
		{
			var json     = content[(Constants.Composer.ItemDraftEnvelopePrefix.Length + 1)..];
			var envelope = JsonSerializer.Deserialize<Envelope>(json, TdcpJsonOptions.Default);
			return new ItemDraftModel(envelope?.Text ?? string.Empty, envelope?.Questions is { Count: > 0 } ? envelope.Questions : null);
		}
		catch (JsonException)
		{
			return new ItemDraftModel(content, null);   // damaged: show it rather than lose it
		}
	}

	private sealed class Envelope
	{
		public string Text { get; set; } = string.Empty;
		public IReadOnlyList<AttentionQuestionAnswerParams>? Questions { get; set; }
	}
}
