using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Tessyn.Web.Auth;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>
/// The real Tessyn.Web host in memory, with a known login, link token and temp state folders.
/// <see cref="PasswordHasher"/> only names the Tessyn.Web assembly (its <c>Program</c> is internal).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class E2EWebFactory : WebApplicationFactory<PasswordHasher>
{
	public const string Email = "vitaly@example.test";
	public const string Password = "e2e correct horse battery staple";
	public const string LinkToken = "e2e-link-token-0123456789-abcdefghij";
	public const string ClientHeader = "X-Tessyn-Client";
	public const string ClientValue = "web";

	private const int TestIterations = 1000;

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-e2e-web-" + Guid.NewGuid().ToString("N"));

	public static string LinkTokenSha256() =>
		Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(LinkToken)));

	public static string HashPassword() => new PasswordHasher().Hash(Password, TestIterations);

	public HttpClient CreateBrowser()
	{
		var client = CreateClient(new WebApplicationFactoryClientOptions
		{
			BaseAddress = new Uri("https://localhost"),
			HandleCookies = true,
		});
		client.DefaultRequestHeaders.Add(ClientHeader, ClientValue);
		return client;
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.UseSetting("TessynWeb:Auth:Email", Email);
		builder.UseSetting("TessynWeb:Auth:PasswordHash", HashPassword());
		builder.UseSetting("TessynWeb:Link:TokenSha256", LinkTokenSha256());
		builder.UseSetting("TessynWeb:DataProtectionKeysPath", Path.Combine(_root, "keys"));
		builder.UseSetting("TessynWeb:Auth:EpochPath", Path.Combine(_root, "auth-epoch"));
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (!disposing || !Directory.Exists(_root))
			return;

		try
		{
			Directory.Delete(_root, true);
		}
		catch (IOException)
		{
			// Temp keys only.
		}
	}
}
