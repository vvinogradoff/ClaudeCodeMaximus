using System.Collections.Generic;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>Reads the <c>questions</c> param of <c>attention.reply</c> and <c>draft.set</c> (FR.23.36, FR.23.37); a wrong shape throws <see cref="TdcpInvalidParamsException"/> (-32602).</summary>
/// <remarks>Created by Claude</remarks>
public static class TdcpQuestionAnswerParams
{
	/// <summary>The questions of <paramref name="owner"/> (an answer or the draft), or null when the key is absent or null.</summary>
	public static IReadOnlyList<AttentionQuestionAnswerParams>? Optional(JsonNode? owner)
	{
		var node = TdcpParams.Find(owner, "questions");
		if (node == null)
			return null;

		if (node is not JsonArray array)
			throw new TdcpInvalidParamsException("'questions' must be an array");

		var result = new List<AttentionQuestionAnswerParams>(array.Count);
		foreach (var element in array)
		{
			if (element is not JsonObject)
				throw new TdcpInvalidParamsException("'questions' must contain objects");

			var number = TdcpParams.OptionalInt(element, "number")
				?? throw new TdcpInvalidParamsException("'questions' entries need an integer 'number'");
			result.Add(new AttentionQuestionAnswerParams(number, TdcpParams.OptionalString(element, "value"), TdcpListParams.OptionalStrings(element, "choices")));
		}

		return result;
	}
}
