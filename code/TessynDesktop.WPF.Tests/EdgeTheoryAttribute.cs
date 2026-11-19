namespace TessynDesktop.WPF.Tests;

/// <summary>A theory that is reported as skipped, with the reason, when Microsoft Edge is not installed.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class EdgeTheoryAttribute : TheoryAttribute
{
	public EdgeTheoryAttribute()
	{
		if (new EdgeBrowser().Find() == null)
			Skip = "Microsoft Edge is not installed";
	}
}