using System;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.ViewModels;

/// <summary>One chip of the Tag row (FR.24.7): label, URL and the two-line tooltip.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagChipViewModel
{
	private readonly SessionTagLauncher _launcher;

	public SessionTagChipViewModel(SessionTagModel tag, SessionTagLauncher launcher)
	{
		_launcher = launcher;
		Label = tag.Label;
		Url = tag.Url;
		Tooltip = $"{tag.Url}\n{BuildSourceLine(tag)}";
	}

	public string Label { get; }
	public string Url { get; }

	/// <summary>Line 1: the URL. Line 2: the source, with the local time for declared tags.</summary>
	public string Tooltip { get; }

	public void Open() => _launcher.Open(Url);

	private static string BuildSourceLine(SessionTagModel tag)
	{
		if (tag.Source == SessionTagSource.Name || tag.AddedUtc is null)
			return Constants.Tags.TooltipFromName;

		var local = tag.AddedUtc.Value.ToLocalTime();
		var format = local.Date == DateTime.Today ? Constants.Tags.TimeTodayFormat : Constants.Tags.TimeOtherDayFormat;
		return string.Format(Constants.Tags.TooltipTaggedFormat, local.ToString(format));
	}
}
