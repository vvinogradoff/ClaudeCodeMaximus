using System.Net;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Tessyn.Web.Tests.Support;

namespace Tessyn.Web.Tests;

/// <remarks>Created by Claude</remarks>
public sealed class HostingTests : IDisposable
{
	private readonly string _spaRoot = Path.Combine(Path.GetTempPath(), "tessyn-web-tests-spa-" + Guid.NewGuid().ToString("N"));

	[Fact]
	public async Task Healthz_reports_the_link_state_without_auth()
	{
		using var factory = new TessynWebFactory();
		using var http = factory.CreateClient();

		var offline = JsonDocument.Parse(await http.GetStringAsync(Constants.Paths.Health));
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		var online = JsonDocument.Parse(await http.GetStringAsync(Constants.Paths.Health));

		Assert.True(offline.RootElement.GetProperty("ok").GetBoolean());
		Assert.False(offline.RootElement.GetProperty("linkOnline").GetBoolean());
		Assert.True(online.RootElement.GetProperty("linkOnline").GetBoolean());
	}

	[Fact]
	public async Task Responses_carry_the_security_headers_and_api_is_not_cached()
	{
		using var factory = new TessynWebFactory();
		using var http = factory.CreateClient();

		var response = await http.GetAsync(Constants.Paths.Health);

		Assert.Equal(Constants.HeaderValues.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
		Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
		Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
		Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
		Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
	}

	[Fact]
	public async Task Spa_fallback_serves_the_built_in_page_when_the_spa_is_not_built()
	{
		Directory.CreateDirectory(_spaRoot);
		using var factory = new TessynWebFactory { SpaFiles = new PhysicalFileProvider(_spaRoot) };
		using var http = factory.CreateClient();

		var response = await http.GetAsync("/some/client/route");
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("not been built", body, StringComparison.Ordinal);
		Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
	}

	[Fact]
	public async Task Spa_fallback_serves_index_html_for_client_routes_and_static_files_directly()
	{
		Directory.CreateDirectory(Path.Combine(_spaRoot, "assets"));
		File.WriteAllText(Path.Combine(_spaRoot, "index.html"), "<html>SPA-INDEX</html>");
		File.WriteAllText(Path.Combine(_spaRoot, "assets", "app.js"), "console.log('x')");
		File.WriteAllText(Path.Combine(_spaRoot, "manifest.webmanifest"), "{}");
		using var factory = new TessynWebFactory { SpaFiles = new PhysicalFileProvider(_spaRoot) };
		using var http = factory.CreateClient();

		var route = await http.GetAsync("/session/abc");
		var root = await http.GetAsync("/");
		var asset = await http.GetAsync("/assets/app.js");
		var manifest = await http.GetAsync("/manifest.webmanifest");

		Assert.Contains("SPA-INDEX", await route.Content.ReadAsStringAsync(), StringComparison.Ordinal);
		Assert.Contains("SPA-INDEX", await root.Content.ReadAsStringAsync(), StringComparison.Ordinal);
		Assert.Equal("no-cache", route.Headers.CacheControl!.ToString());
		Assert.Equal("console.log('x')", await asset.Content.ReadAsStringAsync());
		Assert.Contains("immutable", asset.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
		Assert.Equal("application/manifest+json", manifest.Content.Headers.ContentType!.MediaType);
		Assert.True(route.Headers.Contains("Content-Security-Policy"));
	}

	[Fact]
	public async Task Unknown_api_and_link_gets_are_404_not_the_spa()
	{
		File.WriteAllText(Path.Combine(Directory.CreateDirectory(_spaRoot).FullName, "index.html"), "<html>SPA-INDEX</html>");
		using var factory = new TessynWebFactory { SpaFiles = new PhysicalFileProvider(_spaRoot) };
		using var http = factory.CreateClient();

		var api = await http.GetAsync("/api/nothing");
		var link = await http.GetAsync("/link/nothing");

		Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, link.StatusCode);
	}

	[Fact]
	public async Task Json_responses_are_compressed_when_the_client_accepts_gzip()
	{
		using var factory = new TessynWebFactory();
		using var http = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
		var request = new HttpRequestMessage(HttpMethod.Get, Constants.Paths.Health);
		request.Headers.AcceptEncoding.ParseAdd("gzip");

		var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

		Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
	}

	public void Dispose()
	{
		if (Directory.Exists(_spaRoot))
			Directory.Delete(_spaRoot, true);
	}
}
