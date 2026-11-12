using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Tessyn.Web.Auth;
using Tessyn.Web.Hosting;

namespace Tessyn.Web.Tests.Support;

/// <summary>Test host with a known login, link token and tweakable settings.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynWebFactory : WebApplicationFactory<Program>
{
	public const string Email = "vitaly@example.test";
	public const string Password = "correct horse battery staple";
	public const string LinkToken = "test-link-token-0123456789";
	public const string DaemonName = "TESTPC";
	public const string ClientHeader = "X-Tessyn-Client";

	/// <summary>Test-only header: the host copies it into Connection.RemoteIpAddress (TestServer has no real peer address).</summary>
	public const string RemoteIpHeader = "X-Test-Remote-Ip";

	private const int TestIterations = 1000;
	private static readonly string PasswordHash = new PasswordHasher().Hash(Password, TestIterations);

	private readonly Dictionary<string, string> _settings = new();
	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-web-tests-" + Guid.NewGuid().ToString("N"));

	public TessynWebFactory()
	{
		Set("TessynWeb:Auth:Email", Email);
		Set("TessynWeb:Auth:PasswordHash", PasswordHash);
		Set("TessynWeb:Link:TokenSha256", LinkTokenSha256());
		Set("TessynWeb:DataProtectionKeysPath", Path.Combine(_root, "keys"));
		Set("TessynWeb:Auth:EpochPath", Path.Combine(_root, "auth-epoch"));
	}

	public IFileProvider? SpaFiles { get; set; }
	public TimeProvider? Time { get; set; }

	public static string HashFor(string password)
	{
		return new PasswordHasher().Hash(password, TestIterations);
	}

	/// <summary>Points this host at another host's key ring and epoch file, as a restart (or a second instance) would.</summary>
	public TessynWebFactory SharingStateWith(TessynWebFactory other)
	{
		Set("TessynWeb:DataProtectionKeysPath", other._settings["TessynWeb:DataProtectionKeysPath"]);
		Set("TessynWeb:Auth:EpochPath", other._settings["TessynWeb:Auth:EpochPath"]);
		return this;
	}

	public static string LinkTokenSha256()
	{
		return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(LinkToken)));
	}

	public TessynWebFactory Set(string key, string value)
	{
		_settings[key] = value;
		return this;
	}

	public TessynWebFactory Without(string key)
	{
		_settings.Remove(key);
		return this;
	}

	/// <summary>An https client (the auth cookie is Secure) that keeps cookies and sends the CSRF header.</summary>
	public HttpClient CreateBrowser()
	{
		var client = CreateClient(new WebApplicationFactoryClientOptions
		{
			BaseAddress = new Uri("https://localhost"),
			HandleCookies = true,
		});
		client.DefaultRequestHeaders.Add(ClientHeader, Constants.Headers.ClientValue);
		return client;
	}

	public async Task<HttpClient> CreateLoggedInBrowserAsync()
	{
		var client = CreateBrowser();
		var response = await client.PostAsJsonAsync(Constants.Paths.Login, new Models.LoginRequest(Email, Password));
		response.EnsureSuccessStatusCode();
		return client;
	}

	public HttpClient CreateDaemonHttp()
	{
		return CreateClient();
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		foreach (var (key, value) in _settings)
			builder.UseSetting(key, value);

		builder.ConfigureTestServices(services =>
		{
			services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, RemoteIpStartupFilter>();
			if (SpaFiles is not null)
				services.AddSingleton(new SpaFiles(SpaFiles));
			if (Time is not null)
				services.AddSingleton(Time);
		});
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing && Directory.Exists(_root))
		{
			try
			{
				Directory.Delete(_root, true);
			}
			catch (IOException)
			{
				// Best effort cleanup of temp keys.
			}
		}
	}
}

/// <summary>Lets a test choose the connection's remote address per request.</summary>
file sealed class RemoteIpStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
	public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next)
	{
		return app =>
		{
			app.Use((context, pipeline) =>
			{
				if (context.Request.Headers.TryGetValue(TessynWebFactory.RemoteIpHeader, out var ip)
					&& System.Net.IPAddress.TryParse(ip.ToString(), out var address))
					context.Connection.RemoteIpAddress = address;
				return pipeline(context);
			});
			next(app);
		};
	}
}
