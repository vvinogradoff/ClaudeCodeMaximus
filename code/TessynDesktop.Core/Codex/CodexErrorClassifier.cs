using System.Text.Json;
using TessynDesktop.Codex.Events;
using TessynDesktop.Codex.Protocol;

namespace TessynDesktop.Codex;

/// <summary>
/// Classifies a Codex turn error by its structured <c>codexErrorInfo</c> only (FR.33.13), never by its text: the code is the string value,
/// or the name of the one member of an object value (for example <c>{"httpConnectionFailed": {…}}</c>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexErrorClassifier
{
	/// <summary>The error event of a turn error, with its kind, code and SYSTEM entry text.</summary>
	public CodexTurnErrorEvent ToErrorEvent(CodexTurnError error)
	{
		var code = CodeOf(error.CodexErrorInfo);
		var kind = KindOf(code);

		var systemText = kind == CodexErrorKind.ContextWindowExceeded
			? Constants.Codex.Texts.ContextWindowExceeded
			: code is null
				? string.Format(Constants.Codex.Texts.ErrorFormat, error.Message)
				: string.Format(Constants.Codex.Texts.ErrorWithCodeFormat, error.Message, code);

		return new CodexTurnErrorEvent(error.Message, kind, code, systemText);
	}

	public string? CodeOf(JsonElement? codexErrorInfo)
	{
		if (codexErrorInfo is not { } info)
			return null;

		if (info.ValueKind == JsonValueKind.String)
			return info.GetString();

		if (info.ValueKind == JsonValueKind.Object)
		{
			foreach (var member in info.EnumerateObject())
				return member.Name;
		}

		return null;
	}

	public CodexErrorKind KindOf(string? code) => code switch
	{
		Constants.Codex.Protocol.ErrorUnauthorized          => CodexErrorKind.Auth,
		Constants.Codex.Protocol.ErrorUsageLimitExceeded    => CodexErrorKind.Limit,
		Constants.Codex.Protocol.ErrorRateLimitExceeded     => CodexErrorKind.Limit,
		Constants.Codex.Protocol.ErrorContextWindowExceeded => CodexErrorKind.ContextWindowExceeded,
		_                                                   => CodexErrorKind.Other,
	};
}
