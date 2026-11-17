using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>The tag cloud font size: default, limits, typed input (Settings › Appearance).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagFontSizeRulesTests
{
	[Fact]
	public void TheDefault_IsTheSizeTheAvaloniaChipsHadBeforeTheSetting()
	{
		Assert.Equal(11, TagFontSizeRules.Default);
		Assert.Equal(TagFontSizeRules.Default, new AppSettingsModel().TagFontSize);
	}

	[Theory]
	[InlineData(8, 8)]
	[InlineData(11, 11)]
	[InlineData(40, 40)]
	[InlineData(12.5, 12.5)]
	[InlineData(7.9, 11)]
	[InlineData(41, 11)]
	[InlineData(0, 11)]
	[InlineData(-3, 11)]
	[InlineData(double.NaN, 11)]
	[InlineData(double.PositiveInfinity, 11)]
	public void Normalize_KeepsAValidSize_AndFallsBackToTheDefaultOtherwise(double stored, double expected) =>
		Assert.Equal(expected, TagFontSizeRules.Normalize(stored));

	[Theory]
	[InlineData("12", 12)]
	[InlineData(" 14.5 ", 14.5)]
	[InlineData("8", 8)]
	[InlineData("40", 40)]
	public void TryParse_AcceptsNumbersInRange(string text, double expected)
	{
		Assert.True(TagFontSizeRules.TryParse(text, out var size));
		Assert.Equal(expected, size);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	[InlineData("big")]
	[InlineData("7")]
	[InlineData("41")]
	[InlineData("NaN")]
	[InlineData("Infinity")]
	public void TryParse_RefusesBlankTextNonNumbersAndOutOfRange(string? text)
	{
		Assert.False(TagFontSizeRules.TryParse(text, out var size));
		Assert.Equal(TagFontSizeRules.Default, size);
	}

	[Fact]
	public void Format_RoundTripsThroughTryParse()
	{
		Assert.Equal("12.5", TagFontSizeRules.Format(12.5));
		Assert.Equal("11", TagFontSizeRules.Format(11));
		Assert.True(TagFontSizeRules.TryParse(TagFontSizeRules.Format(12.5), out var size));
		Assert.Equal(12.5, size);
	}

	[Fact]
	public void AnOldSettingsFileWithoutTheKey_GetsTheDefault()
	{
		var model = System.Text.Json.JsonSerializer.Deserialize<AppSettingsModel>("""{ "inputFontSize": 15 }""", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

		Assert.Equal(TagFontSizeRules.Default, model!.TagFontSize);
		Assert.Equal(15, model.InputFontSize);
	}
}
