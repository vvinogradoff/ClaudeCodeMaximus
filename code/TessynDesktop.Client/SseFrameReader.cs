namespace TessynDesktop.Client;

/// <summary>Parses a <c>text/event-stream</c> into frames. Comment lines (<c>: ...</c>) are skipped; a blank line ends a frame.</summary>
/// <remarks>Created by Claude</remarks>
internal static class SseFrameReader
{
	private const string IdField = "id:";
	private const string DataField = "data:";

	/// <summary>Reads frames until the stream ends. <paramref name="onActivity"/> runs for every line received, comments included.</summary>
	public static async IAsyncEnumerable<SseFrame> ReadAsync(
		TextReader reader,
		Action onActivity,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		long id = 0;
		var data = new System.Text.StringBuilder();
		var hasData = false;

		while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
		{
			onActivity();

			if (line.Length == 0)
			{
				if (hasData)
					yield return new SseFrame(id, data.ToString());

				id = 0;
				data.Clear();
				hasData = false;
				continue;
			}

			if (line[0] == ':')
				continue;

			if (line.StartsWith(IdField, StringComparison.Ordinal))
			{
				long.TryParse(Strip(line, IdField), out id);
			}
			else if (line.StartsWith(DataField, StringComparison.Ordinal))
			{
				if (hasData)
					data.Append('\n');
				data.Append(Strip(line, DataField));
				hasData = true;
			}
		}
	}

	private static string Strip(string line, string field)
	{
		var value = line[field.Length..];
		return value.StartsWith(' ') ? value[1..] : value;
	}
}
