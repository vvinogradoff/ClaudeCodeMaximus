namespace TessynDesktop.WPF.Tests;

/// <summary>A fact that is reported as skipped, with the reason, when Microsoft Edge is not installed.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class EdgeFactAttribute : FactAttribute
{
	public EdgeFactAttribute()
	{
		if (new EdgeBrowser().Find() == null)
			Skip = "Microsoft Edge is not installed";
	}
}