namespace TessynDesktop.WPF.Views;

/// <summary>
/// Star weights for the columns of a table in a Focus card (FR.2.14): a column's weight follows its text, so a long-text column gets the larger
/// share while every other column keeps about the width of its longest word. WPF star columns have no minimum width, so the extra share is capped:
/// the text beyond the longest word adds a quarter of itself, at most <see cref="MaxExtraWords"/> times the longest word, and a column's weight is at
/// most <see cref="MaxShareOfOthers"/> times the sum of the other columns' longest words. The weight is never below <see cref="FloorWeight"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TableColumnWeights
{
	/// <summary>The smallest weight, so an empty or one-character column stays usable.</summary>
	public const double FloorWeight = 4;

	/// <summary>The extra text adds this share of what the longest cell has beyond the longest word.</summary>
	public const double ExtraTextShare = 0.25;

	/// <summary>The extra share is at most this many times the column's longest word.</summary>
	public const double MaxExtraWords = 4;

	/// <summary>A column's weight is at most this many times the sum of the other columns' longest words (when there are other columns).</summary>
	public const double MaxShareOfOthers = 3;

	/// <summary>One weight per column; <paramref name="rows"/> holds the cell texts of each row (rows may be of different lengths).</summary>
	public IReadOnlyList<double> Compute(IReadOnlyList<IReadOnlyList<string>> rows)
	{
		var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
		var words = new double[columns];
		var cells = new double[columns];
		for (var column = 0; column < columns; column++)
		{
			var longestWord = 0;
			var longestCell = 0;
			foreach (var row in rows)
			{
				if (column >= row.Count)
					continue;

				var text = row[column].Trim();
				longestCell = Math.Max(longestCell, text.Length);
				foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
					longestWord = Math.Max(longestWord, word.Length);
			}

			words[column] = Math.Max(FloorWeight, longestWord);
			cells[column] = longestCell;
		}

		var total = words.Sum();
		var weights = new double[columns];
		for (var column = 0; column < columns; column++)
		{
			var extra = Math.Min(ExtraTextShare * Math.Max(0, cells[column] - words[column]), MaxExtraWords * words[column]);
			var weight = words[column] + extra;
			if (columns > 1)
				weight = Math.Min(weight, Math.Max(words[column], MaxShareOfOthers * (total - words[column])));
			weights[column] = weight;
		}

		return weights;
	}
}