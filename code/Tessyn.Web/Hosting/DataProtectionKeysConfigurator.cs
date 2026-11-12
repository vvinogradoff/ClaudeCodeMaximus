using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Tessyn.Web.Options;

namespace Tessyn.Web.Hosting;

/// <summary>Persists Data Protection keys to the configured directory so cookies survive restarts.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DataProtectionKeysConfigurator : IConfigureOptions<KeyManagementOptions>
{
	private readonly IOptions<TessynWebOptions> _options;
	private readonly ILoggerFactory _loggerFactory;

	public DataProtectionKeysConfigurator(IOptions<TessynWebOptions> options, ILoggerFactory loggerFactory)
	{
		_options = options;
		_loggerFactory = loggerFactory;
	}

	public void Configure(KeyManagementOptions options)
	{
		var path = _options.Value.DataProtectionKeysPath;
		if (string.IsNullOrWhiteSpace(path))
			return;

		var directory = Directory.CreateDirectory(path);
		options.XmlRepository = new FileSystemXmlRepository(directory, _loggerFactory);
	}
}
