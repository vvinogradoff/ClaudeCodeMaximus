using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeAssistServiceTests
{
	// --- Title response parsing ---

	[Fact]
	public void ParseTitleResponse_DirectJsonObject_ReturnsMappings()
	{
		var json = """{"abc-123": "Fix Auth Module", "def-456": "Add Search Feature"}""";

		var result = ClaudeAssistService.ParseTitleResponse(json);

		Assert.Equal(2, result.Count);
		Assert.Equal("Fix Auth Module", result["abc-123"]);
		Assert.Equal("Add Search Feature", result["def-456"]);
	}

	[Fact]
	public void ParseTitleResponse_WrappedInResultField_ReturnsMappings()
	{
		var json = """{"result": "{\"abc-123\": \"Fix Auth Module\", \"def-456\": \"Add Search\"}"}""";

		var result = ClaudeAssistService.ParseTitleResponse(json);

		Assert.Equal(2, result.Count);
		Assert.Equal("Fix Auth Module", result["abc-123"]);
		Assert.Equal("Add Search", result["def-456"]);
	}

	[Fact]
	public void ParseTitleResponse_WithSurroundingText_ExtractsJson()
	{
		var rawOutput = """Here are the titles: {"abc-123": "Fix Bug"} That's all.""";

		var result = ClaudeAssistService.ParseTitleResponse(rawOutput);

		Assert.Single(result);
		Assert.Equal("Fix Bug", result["abc-123"]);
	}

	[Fact]
	public void ParseTitleResponse_EmptyInput_ReturnsEmpty()
	{
		var result = ClaudeAssistService.ParseTitleResponse("");

		Assert.Empty(result);
	}

	[Fact]
	public void ParseTitleResponse_NonsenseInput_ReturnsEmpty()
	{
		var result = ClaudeAssistService.ParseTitleResponse("This is not JSON at all");

		Assert.Empty(result);
	}

	[Fact]
	public void ParseTitleResponse_IgnoresNonStringValues()
	{
		var json = """{"abc-123": "Valid Title", "def-456": 42, "ghi-789": null}""";

		var result = ClaudeAssistService.ParseTitleResponse(json);

		Assert.Single(result);
		Assert.Equal("Valid Title", result["abc-123"]);
	}

	// --- Search response parsing ---

	[Fact]
	public void ParseSearchResponse_DirectJsonArray_ReturnsOrderedIds()
	{
		var json = """["abc-123", "def-456", "ghi-789"]""";

		var result = ClaudeAssistService.ParseSearchResponse(json);

		Assert.Equal(3, result.Count);
		Assert.Equal("abc-123", result[0]);
		Assert.Equal("def-456", result[1]);
		Assert.Equal("ghi-789", result[2]);
	}

	[Fact]
	public void ParseSearchResponse_WrappedInResultField_ReturnsIds()
	{
		var json = """{"result": "[\"abc-123\", \"def-456\"]"}""";

		var result = ClaudeAssistService.ParseSearchResponse(json);

		Assert.Equal(2, result.Count);
		Assert.Equal("abc-123", result[0]);
		Assert.Equal("def-456", result[1]);
	}

	[Fact]
	public void ParseSearchResponse_WithSurroundingText_ExtractsArray()
	{
		var rawOutput = """Based on your query: ["abc-123"] These are the results.""";

		var result = ClaudeAssistService.ParseSearchResponse(rawOutput);

		Assert.Single(result);
		Assert.Equal("abc-123", result[0]);
	}

	[Fact]
	public void ParseSearchResponse_EmptyInput_ReturnsEmpty()
	{
		var result = ClaudeAssistService.ParseSearchResponse("");

		Assert.Empty(result);
	}

	[Fact]
	public void ParseSearchResponse_EmptyArray_ReturnsEmpty()
	{
		var result = ClaudeAssistService.ParseSearchResponse("[]");

		Assert.Empty(result);
	}

	[Fact]
	public void ParseSearchResponse_SkipsNonStringElements()
	{
		var json = """["abc-123", 42, null, "def-456"]""";

		var result = ClaudeAssistService.ParseSearchResponse(json);

		Assert.Equal(2, result.Count);
		Assert.Equal("abc-123", result[0]);
		Assert.Equal("def-456", result[1]);
	}

	// --- Model fallback order ---

	[Fact]
	public void GetModelFallbackOrder_Default_ReturnsHaikuThenNull()
	{
		var models = ClaudeAssistService.GetModelFallbackOrderFromIndex(0);

		Assert.Equal(2, models.Count);
		Assert.Equal("haiku", models[0]);
		Assert.Null(models[1]);
	}

	[Fact]
	public void GetModelFallbackOrder_UserSelectedOpus_ReturnsHaikuOpusNull()
	{
		var models = ClaudeAssistService.GetModelFallbackOrderFromIndex(1);

		Assert.Equal(3, models.Count);
		Assert.Equal("haiku", models[0]);
		Assert.Equal("opus", models[1]);
		Assert.Null(models[2]);
	}

	[Fact]
	public void GetModelFallbackOrder_UserSelectedHaiku_NoDuplicate()
	{
		var models = ClaudeAssistService.GetModelFallbackOrderFromIndex(3);

		// haiku + null (no duplicate haiku)
		Assert.Equal(2, models.Count);
		Assert.Equal("haiku", models[0]);
		Assert.Null(models[1]);
	}
}
