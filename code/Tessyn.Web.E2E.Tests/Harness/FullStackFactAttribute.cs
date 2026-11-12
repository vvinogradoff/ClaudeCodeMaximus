namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>A fact that only runs when <c>TESSYN_FULLSTACK=1</c> (it starts a real server, a browser and builds the SPA, so it is slow).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FullStackFactAttribute : FactAttribute
{
	public const string EnableVariable = "TESSYN_FULLSTACK";

	public FullStackFactAttribute()
	{
		if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
			Skip = $"Full-stack UI run: set {EnableVariable}=1 to enable.";
	}
}
