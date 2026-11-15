namespace TessynDesktop.WPF.Rendering;

/// <summary>One transcript entry as the page shows it: header text and sanitized HTML, rendered off the UI thread.</summary>
/// <param name="Index">The entry's index in the session (absolute, stable within an epoch).</param>
/// <param name="Role">Role code: U (user), A (assistant), S (system), C (compaction).</param>
/// <param name="Timestamp">Header text after the role label (local time and model/effort).</param>
/// <param name="Html">Body HTML from Markdig with raw HTML disabled and links filtered.</param>
/// <param name="Truncated">The daemon cut the content; the page offers "show full entry".</param>
/// <param name="FullLength">Length of the uncut content when <paramref name="Truncated"/>.</param>
/// <param name="EstimatedHeight">Initial height guess in pixels, replaced by the measured height.</param>
/// <param name="Media">https media links of an assistant entry (FR.26.4), at most six; the page decides per link whether it loads at once or after a click.</param>
/// <param name="Paths">Absolute local file paths of an assistant entry (FR.26.5); the page shows each as a chip.</param>
/// <param name="Source">What produced a user entry ("scheduled", "orchestrated", "telegram", "web"); null for the desktop user and other roles. The page colours scheduled and orchestrated inputs apart.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TranscriptEntryView(
	int Index,
	string Role,
	string Timestamp,
	string Html,
	bool Truncated,
	int FullLength,
	int EstimatedHeight,
	IReadOnlyList<string>? Media = null,
	IReadOnlyList<string>? Paths = null,
	string? Source = null);
