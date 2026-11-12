using Microsoft.Extensions.FileProviders;

namespace Tessyn.Web.Hosting;

/// <summary>Where the built SPA lives (wwwroot by default; replaceable in tests).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SpaFiles
{
	public SpaFiles(IFileProvider provider)
	{
		Provider = provider;
	}

	public IFileProvider Provider { get; }
}
