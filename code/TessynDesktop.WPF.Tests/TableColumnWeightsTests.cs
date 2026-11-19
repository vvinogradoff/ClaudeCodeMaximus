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