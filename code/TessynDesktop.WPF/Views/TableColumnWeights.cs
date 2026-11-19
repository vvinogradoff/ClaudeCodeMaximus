namespace TessynDesktop.WPF.Views;

/// <summary>
/// Star weights for the columns of a table in a Focus card (FR.2.14): a column's weight follows its text, so a long-text column gets the larger
/// share and a short column keeps about the width of its longest word. The weight is the longest word, plus a quarter of what the longest cell
/// has beyond it, and never below <see cref="FloorWeight"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TableColumnWeights
{
	/// <summary>The smallest weight, so an empty or one-character column stays usable.</summary>
	public const double FloorWeight = 4;

	private const double ExtraTextShare = 0.25;

	/// <summary>One weight per column; <paramref name="rows"/> holds the cell texts of each row (rows may be of different lengths).</summary>
	public IReadOnlyList<double> Compute(IReadOnlyList<IReadOnlyList<string>> rows)
	{
		var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
		var weights = new double[columns];
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

			weights[column] = Math.Max(FloorWeight, longestWord + ExtraTextShare * (longestCell - longestWord));
		}

		return weights;
	}
}
