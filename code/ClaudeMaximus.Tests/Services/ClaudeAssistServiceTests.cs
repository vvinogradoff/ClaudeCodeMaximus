using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using Xunit;

namespace ClaudeMaximus.Tests.Services;

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

	// --- Model fallback order (FR.13.14) ---

	private static readonly IReadOnlyList<ClaudeModelInfo> SampleModels =
	[
		new("claude-opus-4-7",           "opus",   "Opus 4.7"),
		new("claude-sonnet-4-6",         "sonnet", "Sonnet 4.6"),
		new("claude-haiku-4-5-20251001", "haiku",  "Haiku 4.5"),
		new("gemma4:26b",                "",       "gemma4:26b", ModelProvider.Ollama),
	];

	[Fact]
	public void GetModelFallbackOrder_DefaultEmpty_ReturnsHaikuThenNull()
	{
		var models = ClaudeAssistService.GetModelFallbackOrderFromId(string.Empty, SampleModels);

		Assert.Equal(2, models.Count);
		Assert.Equal("haiku", models[0]);
		Assert.Null(models[1]);
	}

	[Fact]
	public void GetModelFallbackOrder_UserSelectedOpus_ReturnsHaikuOpusNull()
	{
		var models = ClaudeAssistService.GetModelFallbackOrderFromId("claude-opus-4-7", SampleModels);

		Assert.Equal(3, models.Count);
		Assert.Equal("haiku", models[0]);
		Assert.Equal("opus", models[1]); // alias used for Anthropic models
		Assert.Null(models[2]);
	}

	[Fact]
	public void GetModelFallbackOrder_UserSelectedHaiku_NoDuplicate()
	{
		var models = ClaudeAssistService.GetModelFallbackOrderFromId("claude-haiku-4-5-20251001", SampleModels);

		// haiku + null (no duplicate haiku)
		Assert.Equal(2, models.Count);
		Assert.Equal("haiku", models[0]);
		Assert.Null(models[1]);
	}

	[Fact]
	public void GetModelFallbackOrder_OllamaModel_Excluded()
	{
		// Ollama models must be skipped — assist calls use Anthropic only (FR.12.14)
		var models = ClaudeAssistService.GetModelFallbackOrderFromId("gemma4:26b", SampleModels);

		Assert.Equal(2, models.Count);
		Assert.Equal("haiku", models[0]);
		Assert.Null(models[1]);
	}

	[Fact]
	public void GetModelFallbackOrder_UnknownId_IncludedAsLiteral()
	{
		// Unknown ID (not in model list) — treated as Anthropic, passed through as literal
		var models = ClaudeAssistService.GetModelFallbackOrderFromId("claude-future-model", SampleModels);

		Assert.Equal(3, models.Count);
		Assert.Equal("haiku", models[0]);
		Assert.Equal("claude-future-model", models[1]);
		Assert.Null(models[2]);
	}
}
