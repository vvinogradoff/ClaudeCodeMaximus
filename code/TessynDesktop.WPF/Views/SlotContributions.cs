using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>Creates the views of a slot's contributions; one failing factory is logged and skipped so it cannot take its neighbours or the window down.</summary>
/// <remarks>Created by Claude</remarks>
internal static class SlotContributions
{
	/// <summary>The view, or null when the factory threw or returned null (logged).</summary>
	public static UIElement? TryCreate(ShellSlotContribution contribution, ILogger? logger)
	{
		try
		{
			var view = contribution.Create();
			if (view != null)
				return view;

			logger?.LogError("A contribution to slot {Slot} created no view and is skipped", contribution.SlotId);
		}
		catch (Exception ex)
		{
			logger?.LogError(ex, "A contribution to slot {Slot} failed to create its view and is skipped", contribution.SlotId);
		}

		return null;
	}

	public static ILogger? ResolveLogger(ILogger? explicitLogger) =>
		explicitLogger ?? new ShellServiceProvider().Get<ILoggerFactory>()?.CreateLogger("TessynDesktop.WPF.Slots");
}
