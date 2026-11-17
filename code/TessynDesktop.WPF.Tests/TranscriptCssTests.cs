using System.Text.RegularExpressions;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The vertical rhythm of the transcript page matches the Avalonia message blocks (SessionView.axaml): a row's own vertical margin is 4 px for a user
/// or system row, 6 px for an assistant row and 8 px for a compaction row, and the text inside a block adds no extra space at its top or bottom.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TranscriptCssTests
{
	private static string Css()
	{
		var path = Path.Combine(AppContext.BaseDirectory, "Web", "transcript.html");
		return File.ReadAllText(path);
	}

	private static string Rule(string selector)
	{
		var m = Regex.Match(Css(), @"(?m)^" + Regex.Escape(selector) + @"\{([^}]*)\}");
		Assert.True(m.Success, "no rule " + selector);
		return m.Groups[1].Value;
	}

	[Fact]
	public void UserAndSystemRowsHaveFourPixelsAbove_AndBelow()
	{
		Assert.Contains("padding:4px 10px", Rule(".e"));
		Assert.DoesNotContain("padding-top", Rule(".e.S"));
		Assert.DoesNotContain("padding-top", Rule(".e.U"));
	}

	[Fact]
	public void AnAssistantRowHasSixPixelsAbove_AndBelow_AndItsTextAddsNone()
	{
		var row = Rule(".e.A");
		Assert.Contains("padding-top:6px", row);
		Assert.Contains("padding-bottom:6px", row);
		Assert.Contains("padding:0 0 0 2px", Rule(".e.A .c"));
	}

	[Fact]
	public void ACompactionRowHasEightPixelsAbove_AndBelow()
	{
		var row = Rule(".e.C");
		Assert.Contains("padding-top:8px", row);
		Assert.Contains("padding-bottom:8px", row);
	}

	[Fact]
	public void TheFirstAndLastBlockInsideAnEntryCarryNoOuterMargin()
	{
		var rule = Css();
		Assert.Contains(".b>:first-child{margin-top:0}", rule);
		Assert.Contains(".b>:last-child{margin-bottom:0}", rule);
	}
}
