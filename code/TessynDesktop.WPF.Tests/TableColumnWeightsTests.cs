using System.Text.RegularExpressions;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.2.14 (TC.MT.01, 02, 04): Focus column weights follow the text; the HTML tables never squeeze a column below its longest word.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TableColumnWeightsTests
{
	private static IReadOnlyList<IReadOnlyList<string>> Rows(params string[][] rows) => rows;

	[Fact]
	public void ALongTextColumn_GetsTheLargerShare_AndAShortColumnKeepsItsLongestWord()
	{
		var weights = new TableColumnWeights().Compute(Rows(
			["#", "Status", "Description"],
			["1", "done", "This column carries a long sentence of ordinary words that should wrap inside its cell"]));

		Assert.True(weights[2] > weights[1]);
		Assert.True(weights[1] >= "Status".Length);
		Assert.True(weights[2] >= "ordinary".Length);
	}

	[Fact]
	public void OneVeryLongCell_DoesNotStarveTheShortColumns_OnA600PixelCard()
	{
		const double CardWidth = 600;
		const double AverageCharWidth = 7;   // px of an average character at the card text size
		var longCell = string.Join(' ', Enumerable.Repeat("ordinary", 75));   // 600 characters
		var rows = Rows(["Id", "Status", "Owner", longCell], ["1", "done", "me", "x"]);

		var weights = new TableColumnWeights().Compute(rows);

		var total = weights.Sum();
		Assert.Equal(4, weights.Count);
		for (var column = 0; column < 3; column++)
		{
			var share = CardWidth * weights[column] / total;
			var longestWord = rows.Max(r => r[column].Length);
			Assert.True(share >= longestWord * AverageCharWidth, $"column {column}: {share:F0}px < {longestWord * AverageCharWidth}px");
		}

		Assert.True(weights[3] > weights[1]);
	}

	[Fact]
	public void EmptyOrOneCharacterColumns_KeepTheFloor()
	{
		var weights = new TableColumnWeights().Compute(Rows(["", "x"], ["", "y"]));

		Assert.Equal([TableColumnWeights.FloorWeight, TableColumnWeights.FloorWeight], weights);
	}

	[Fact]
	public void RowsOfDifferentLength_AndNoRows_AreHandled()
	{
		var weights = new TableColumnWeights().Compute(Rows(["a", "bbbbbbbbbbbb"], ["c"]));

		Assert.Equal(2, weights.Count);
		Assert.Empty(new TableColumnWeights().Compute([]));
	}

	[Fact]
	public void TheTcPwTable_InAFocusCard_GivesTheTitleColumnTheLargestShare_AndNoColumnLessThanItsLongestWord()
	{
		var lines = File.ReadAllLines(FindRepoFile("docs/testcases/TC.PW.01-Preview-Windows.md")).Where(l => l.StartsWith("| TC.PW.", StringComparison.Ordinal)).ToList();
		var rows = lines.Select(l => (IReadOnlyList<string>)l.Trim('|').Split('|').Select(c => c.Trim()).ToList()).ToList();
		Assert.NotEmpty(rows);

		var weights = new TableColumnWeights().Compute(rows);

		Assert.Equal(5, weights.Count);
		Assert.Equal(1, weights.Select((w, i) => (w, i)).OrderByDescending(x => x.w).First().i);   // Title
		for (var column = 0; column < 5; column++)
			Assert.True(weights[column] >= rows.Max(r => r[column].Split(' ', StringSplitOptions.RemoveEmptyEntries).Max(w => w.Length)));
	}

	private static string FindRepoFile(string relative)
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
		{
			var candidate = Path.Combine(dir.FullName, relative);
			if (File.Exists(candidate))
				return candidate;
		}

		throw new FileNotFoundException(relative);
	}

	[Theory]
	[InlineData("transcript.html", ".b table", ".b th,.b td")]
	[InlineData("preview.html", ".md table", ".md th,.md td")]
	public void HtmlTables_WrapAtSpacesOnly_ScrollInsideTheirOwnBox_AndHaveNoCellCap(string page, string tableSelector, string cellSelector)
	{
		var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Web", page));
		var table = Regex.Match(css, Regex.Escape(tableSelector) + @"\{([^}]*)\}").Groups[1].Value;
		var cells = Regex.Match(css, Regex.Escape(cellSelector) + @"\{([^}]*)\}").Groups[1].Value;

		Assert.Contains("display:block", table);
		Assert.Contains("max-width:100%", table);
		Assert.Contains("overflow-x:auto", table);
		Assert.Contains("overflow-wrap:break-word", table);
		Assert.Contains("word-break:normal", table);
		Assert.Contains("overflow-wrap:break-word", cells);
		Assert.Contains("word-break:normal", cells);
		Assert.DoesNotContain("max-width", cells);
		Assert.DoesNotContain("anywhere", table + cells);
	}
}