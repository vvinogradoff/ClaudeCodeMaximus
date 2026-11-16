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

	/// <summary>The 5-hour meter label: the percentage, and (unless the reading is stale) when the window resets, in local time.</summary>
	public static string FiveHour(UsageWindowDto window) =>
		window.IsStale
			? string.Format(CultureInfo.CurrentCulture, Constants.StatusBar.FiveHourFormat, window.Utilization)
			: string.Format(CultureInfo.CurrentCulture, Constants.StatusBar.FiveHourResetFormat, window.Utilization, window.ResetsAtUtc.ToLocalTime());

	/// <summary>The 7-day meter label: the percentage, and (unless stale) the weekday of the reset.</summary>
	public static string SevenDay(UsageWindowDto window) =>
		window.IsStale
			? string.Format(CultureInfo.CurrentCulture, Constants.StatusBar.SevenDayFormat, window.Utilization)
			: string.Format(CultureInfo.CurrentCulture, Constants.StatusBar.SevenDayResetFormat, window.Utilization, window.ResetsAtUtc.ToLocalTime());
}
