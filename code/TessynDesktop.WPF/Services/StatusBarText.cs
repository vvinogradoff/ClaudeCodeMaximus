using System.Globalization;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Services;

/// <summary>The words of the status bar: the model line and the two usage meter labels (FR.18).</summary>
/// <remarks>Created by Claude</remarks>
public static class StatusBarText
{
	/// <summary>The model line: the model id, then "local" for a model that runs on this machine, or its price per million tokens when the daemon told it.</summary>
	/// <param name="inputPerMillion">Price of one million input tokens in US dollars; null or 0 = unknown.</param>
	/// <param name="outputPerMillion">Price of one million output tokens in US dollars; null or 0 = unknown.</param>
	public static string Model(string? modelId, ModelProvider? provider, double? inputPerMillion = null, double? outputPerMillion = null)
	{
		if (string.IsNullOrEmpty(modelId) || modelId == Constants.TurnTexts.DefaultOption)
			return string.Empty;

		if (provider == ModelProvider.Ollama)
			return $"{modelId}  ·  {Constants.StatusBar.LocalModelSuffix}";

		if (inputPerMillion is > 0 || outputPerMillion is > 0)
			return string.Create(CultureInfo.InvariantCulture, $"{modelId}  ·  in ${inputPerMillion ?? 0:0.00} / out ${outputPerMillion ?? 0:0.00} per 1M");

		return modelId;
	}

	/// <summary>FR.33.10: the model line of a GPT model, its display name and the plan it draws on (no prices).</summary>
	public static string GptModel(string displayName) => $"{displayName}  ·  {Constants.Codex.ChatGptPlanSuffix}";

	/// <summary>The top meter label (Claude's 5-hour window, a ChatGPT profile's primary window): see <see cref="Window"/>.</summary>
	public static string FiveHour(UsageWindowDto window) => Window(window, Constants.StatusBar.FiveHourId, Constants.Codex.FiveHourMinutes);

	/// <summary>The bottom meter label (Claude's 7-day window, a ChatGPT profile's secondary window): see <see cref="Window"/>.</summary>
	public static string SevenDay(UsageWindowDto window) => Window(window, Constants.StatusBar.SevenDayId, Constants.Codex.SevenDayMinutes);

	/// <summary>
	/// A meter label: the window's own label (<c>5h</c>, <c>7d</c>, <c>&lt;n&gt;h</c>, FR.33.10; an older daemon sends none, then the slot's), the percentage
	/// and, unless the reading is stale, the reset in local time: the time of day for a window of a day or less, the weekday for a longer one (FR.18.3).
	/// </summary>
	public static string Window(UsageWindowDto window, string fallbackLabel, int fallbackMinutes)
	{
		var label = string.IsNullOrEmpty(window.Label) ? fallbackLabel : window.Label;
		if (window.IsStale)
			return string.Format(CultureInfo.CurrentCulture, Constants.Codex.WindowFormat, label, window.Utilization);

		var shortWindow = (window.DurationMinutes ?? fallbackMinutes) <= Constants.Codex.ShortWindowMaxMinutes;
		return string.Format(CultureInfo.CurrentCulture, shortWindow ? Constants.Codex.WindowShortResetFormat : Constants.Codex.WindowLongResetFormat,
			label, window.Utilization, window.ResetsAtUtc.ToLocalTime());
	}

	/// <summary>FR.33.10: the tooltip of a window that ran out of credits, with Codex's reason; null for any other window.</summary>
	public static string? ExhaustedToolTip(UsageWindowDto? window, string? reason) =>
		window?.IsExhausted == true
			? string.Format(CultureInfo.CurrentCulture, Constants.Codex.ExhaustedToolTipFormat,
				string.IsNullOrEmpty(reason) ? Constants.Codex.ExhaustedReasonUnknown : reason, window.ResetsAtUtc.ToLocalTime())
			: null;
}
