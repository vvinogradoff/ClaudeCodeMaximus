using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.CR.01 to TC.CR.05: the trigger parser in session mode (Code references off, FR.7.14) and the modes that must not change.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AutocompleteTriggerParserTests
{
	private readonly AutocompleteTriggerParser _parser = new();

	private AutocompleteTriggerModel Session(string text, int? caret = null) => _parser.Parse(text, caret ?? text.Length, sessionMode: true);

	[Fact]
	public void TCCR01_SingleHash_GivesSessionMode_FromTheHashToTheCaret()
	{
		var trigger = Session("see #alpha");

		Assert.Equal(AutocompleteMode.Session, trigger.Mode);
		Assert.Equal("alpha", trigger.Query);
		Assert.Equal(4, trigger.TriggerStartIndex);
		Assert.Equal(6, trigger.TriggerLength);
	}

	[Fact]
	public void TCCR02_AHashAtLineStartOrAfterWhitespace_Triggers()
	{
		Assert.Equal(AutocompleteMode.Session, Session("#alpha").Mode);
		Assert.Equal(AutocompleteMode.Session, Session("see\t#alpha").Mode);
		Assert.Equal(AutocompleteMode.Session, Session("line one\n#alpha").Mode);
		Assert.Equal(AutocompleteMode.Session, Session("line one\r\n#alpha").Mode);
	}

	[Theory]
	[InlineData("a#b")]
	[InlineData("see a#alpha te")]
	public void TCCR02_AHashInsideAWord_DoesNotTrigger(string text) =>
		Assert.Equal(AutocompleteMode.None, Session(text).Mode);

	[Fact]
	public void TCCR02_DoubleHash_StaysFile_AndADrivePath_StaysPath()
	{
		var file = Session("see ##logo");
		Assert.Equal((AutocompleteMode.File, "logo", 4), (file.Mode, file.Query, file.TriggerStartIndex));

		var path = Session(@"open C:\Users\x");
		Assert.Equal((AutocompleteMode.Path, @"C:\Users\x"), (path.Mode, path.Query));
	}

	[Fact]
	public void TCCR02_DoubleHash_WithSpacesAfterIt_IsNeverASessionQuery()
	{
		Assert.Equal(AutocompleteMode.None, Session("##logo png").Mode);
		Assert.Equal(AutocompleteMode.None, Session("see ##logo png").Mode);
	}

	[Fact]
	public void TCCR02_ADrivePathAfterTheQuery_WinsOverTheSession()
	{
		var trigger = Session(@"#alpha C:\Users\");

		Assert.Equal(AutocompleteMode.Path, trigger.Mode);
	}

	[Fact]
	public void TCCR03_SpacesBelongToTheQuery_AndOnlyTheTextUpToTheCaretCounts()
	{
		var atEnd = Session("#alpha te");
		Assert.Equal(("alpha te", 0, 9), (atEnd.Query, atEnd.TriggerStartIndex, atEnd.TriggerLength));

		var midText = Session("#alpha te and more", caret: 9);
		Assert.Equal(("alpha te", 0, 9), (midText.Query, midText.TriggerStartIndex, midText.TriggerLength));
	}

	[Fact]
	public void TCCR03_TheNearestHashWins()
	{
		var trigger = Session("#one two #three four");

		Assert.Equal(("three four", 9), (trigger.Query, trigger.TriggerStartIndex));
	}

	[Fact]
	public void TCCR04_ABareHash_HasAnEmptyQuery()
	{
		var trigger = Session("#");

		Assert.Equal((AutocompleteMode.Session, string.Empty), (trigger.Mode, trigger.Query));
	}

	[Theory]
	[InlineData("#alpha\nte")]
	[InlineData("#alpha\r\nte")]
	[InlineData("#alpha\rte")]
	public void TCCR05_ALineBreakBetweenTheHashAndTheCaret_GivesNone(string text) =>
		Assert.Equal(AutocompleteMode.None, Session(text).Mode);

	[Fact]
	public void SessionModeOff_KeepsTheSymbolParsingAsItWas()
	{
		var word = _parser.Parse("see #Fo", 7);
		Assert.Equal((AutocompleteMode.Symbol, "Fo", 4, 3), (word.Mode, word.Query, word.TriggerStartIndex, word.TriggerLength));

		// The symbol query stops at whitespace: a space after it ends the trigger.
		Assert.Equal(AutocompleteMode.None, _parser.Parse("#Fo bar", 7).Mode);
		Assert.Equal(AutocompleteMode.File, _parser.Parse("##Fo", 4).Mode);
	}
}
