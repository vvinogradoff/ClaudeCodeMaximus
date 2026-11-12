using System.Net;
using System.Text.Json;
using Tessyn.Web.Models;
using Tessyn.Web.Tests.Support;

namespace Tessyn.Web.Tests;

/// <remarks>Created by Claude</remarks>
public sealed class AuthTests
{
	[Fact]
	public async Task Login_with_correct_credentials_returns_email_and_a_hardened_cookie()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		var response = await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, TessynWebFactory.Password));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<EmailResponse>();
		Assert.Equal(TessynWebFactory.Email, body!.Email);
		var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(Constants.Auth.CookieName + "=", StringComparison.Ordinal));
		Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Login_email_match_is_case_insensitive()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		var response = await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email.ToUpperInvariant(), TessynWebFactory.Password));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task Login_with_wrong_password_is_401_without_a_cookie()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		var response = await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "nope"));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
		Assert.Equal(Constants.Auth.InvalidCredentialsMessage, body!.Error);
		Assert.False(response.Headers.Contains("Set-Cookie"));
	}

	[Fact]
	public async Task Login_with_unknown_email_is_401_with_the_same_message()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		var response = await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest("someone@else.test", TessynWebFactory.Password));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
		Assert.Equal(Constants.Auth.InvalidCredentialsMessage, body!.Error);
	}

	[Fact]
	public async Task Login_with_an_empty_body_is_401()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		var response = await client.PostAsync(Constants.Paths.Login, new StringContent("{}", System.Text.Encoding.UTF8, Constants.ContentTypes.Json));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Login_is_throttled_after_five_failures_from_one_ip()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		for (var i = 0; i < 5; i++)
		{
			var failure = await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "wrong" + i));
			Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
		}

		var throttled = await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "wrong-again"));

		Assert.Equal((HttpStatusCode)429, throttled.StatusCode);
		Assert.True(throttled.Headers.TryGetValues(Constants.Auth.RetryAfterHeader, out var retry));
		Assert.True(int.Parse(retry!.Single()) > 0);
	}

	[Fact]
	public async Task Correct_password_is_also_rejected_while_throttled()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();
		for (var i = 0; i < 5; i++)
			await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "wrong"));

		var response = await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, TessynWebFactory.Password));

		Assert.Equal((HttpStatusCode)429, response.StatusCode);
	}

	[Fact]
	public async Task Global_failure_limit_throttles_even_a_fresh_ip_bucket()
	{
		using var factory = new TessynWebFactory()
			.Set("TessynWeb:Auth:Throttle:PerIpFailures", "1000")
			.Set("TessynWeb:Auth:Throttle:GlobalFailures", "3");
		using var client = factory.CreateBrowser();

		for (var i = 0; i < 3; i++)
			await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "wrong"));

		var response = await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, TessynWebFactory.Password));

		Assert.Equal((HttpStatusCode)429, response.StatusCode);
	}

	[Fact]
	public async Task Me_requires_the_cookie_and_logout_clears_it()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Constants.Paths.Me)).StatusCode);

		await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, TessynWebFactory.Password));
		var me = await client.GetAsync(Constants.Paths.Me);
		Assert.Equal(HttpStatusCode.OK, me.StatusCode);
		Assert.Equal(TessynWebFactory.Email, (await me.Content.ReadFromJsonAsync<EmailResponse>())!.Email);

		var logout = await client.PostAsync(Constants.Paths.Logout, null);
		Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Constants.Paths.Me)).StatusCode);
	}

	[Fact]
	public async Task Api_calls_without_a_cookie_get_401_not_a_redirect()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		var rpc = await client.PostAsync(Constants.Paths.Rpc, new StringContent("{}", System.Text.Encoding.UTF8, Constants.ContentTypes.Json));
		var events = await client.GetAsync(Constants.Paths.Events, HttpCompletionOption.ResponseHeadersRead);

		Assert.Equal(HttpStatusCode.Unauthorized, rpc.StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, events.StatusCode);
		Assert.Null(rpc.Headers.Location);
	}

	[Fact]
	public async Task Posts_without_the_client_header_are_400()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		browser.DefaultRequestHeaders.Remove(TessynWebFactory.ClientHeader);
		var content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"client.hello\"}", System.Text.Encoding.UTF8, Constants.ContentTypes.Json);

		var rpc = await browser.PostAsync(Constants.Paths.Rpc, content);
		var login = await browser.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, TessynWebFactory.Password));

		Assert.Equal(HttpStatusCode.BadRequest, rpc.StatusCode);
		Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
		using var json = JsonDocument.Parse(await rpc.Content.ReadAsStringAsync());
		Assert.Equal(Constants.Auth.MissingClientHeaderMessage, json.RootElement.GetProperty("error").GetString());
	}
}
