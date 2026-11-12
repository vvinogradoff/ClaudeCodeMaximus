using System.Net;
using Tessyn.Web.Models;
using Tessyn.Web.Tests.Support;

namespace Tessyn.Web.Tests;

/// <summary>SEC-FIX: security stamp, absolute lifetime, trusted device, throttle race, body limits, CSRF header on logout.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AuthHardeningTests
{
	private static readonly LoginRequest Good = new(TessynWebFactory.Email, TessynWebFactory.Password);

	private static string CookieValue(HttpResponseMessage response, string name)
	{
		var header = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(name + "=", StringComparison.Ordinal));
		return header.Split(';')[0];
	}

	/// <summary>A client that does not manage cookies and presents exactly the given Cookie header (a "stolen" cookie).</summary>
	private static HttpClient Replay(TessynWebFactory factory, params string[] cookies)
	{
		var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
		{
			BaseAddress = new Uri("https://localhost"),
			HandleCookies = false,
		});
		client.DefaultRequestHeaders.Add(TessynWebFactory.ClientHeader, Constants.Headers.ClientValue);
		if (cookies.Length > 0)
			client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
		return client;
	}

	[Fact]
	public async Task Auth_cookie_uses_the_host_prefix_with_path_root_and_no_domain()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		var response = await client.PostAsJsonAsync(Constants.Paths.Login, Good);

		Assert.Equal("__Host-Tessyn.Auth", Constants.Auth.CookieName);
		var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-Tessyn.Auth=", StringComparison.Ordinal));
		Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task A_copied_cookie_stops_working_after_logout()
	{
		using var factory = new TessynWebFactory();
		using var owner = factory.CreateBrowser();
		var login = await owner.PostAsJsonAsync(Constants.Paths.Login, Good);
		var stolen = CookieValue(login, Constants.Auth.CookieName);
		using var thief = Replay(factory, stolen);
		Assert.Equal(HttpStatusCode.OK, (await thief.GetAsync(Constants.Paths.Me)).StatusCode);

		Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync(Constants.Paths.Logout, null)).StatusCode);

		Assert.Equal(HttpStatusCode.Unauthorized, (await thief.GetAsync(Constants.Paths.Me)).StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync(Constants.Paths.Me)).StatusCode);
	}

	[Fact]
	public async Task Logout_logs_out_every_device()
	{
		using var factory = new TessynWebFactory();
		using var phone = await factory.CreateLoggedInBrowserAsync();
		using var laptop = await factory.CreateLoggedInBrowserAsync();

		await phone.PostAsync(Constants.Paths.Logout, null);

		Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetAsync(Constants.Paths.Me)).StatusCode);
	}

	[Fact]
	public async Task An_anonymous_logout_does_not_log_anyone_out()
	{
		using var factory = new TessynWebFactory();
		using var owner = await factory.CreateLoggedInBrowserAsync();
		using var anonymous = Replay(factory);

		Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsync(Constants.Paths.Logout, null)).StatusCode);

		Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(Constants.Paths.Me)).StatusCode);
	}

	[Fact]
	public async Task Logout_without_the_client_header_is_400_and_does_not_log_out()
	{
		using var factory = new TessynWebFactory();
		using var owner = await factory.CreateLoggedInBrowserAsync();
		owner.DefaultRequestHeaders.Remove(TessynWebFactory.ClientHeader);

		var logout = await owner.PostAsync(Constants.Paths.Logout, null);

		Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
		Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(Constants.Paths.Me)).StatusCode);
	}

	[Fact]
	public async Task The_login_survives_a_restart_but_not_a_password_change()
	{
		using var first = new TessynWebFactory();
		using var browser = first.CreateBrowser();
		var cookie = CookieValue(await browser.PostAsJsonAsync(Constants.Paths.Login, Good), Constants.Auth.CookieName);

		using var restarted = new TessynWebFactory().SharingStateWith(first);
		using var changed = new TessynWebFactory().SharingStateWith(first).Set("TessynWeb:Auth:PasswordHash", TessynWebFactory.HashFor("a brand new password"));

		Assert.Equal(HttpStatusCode.OK, (await Replay(restarted, cookie).GetAsync(Constants.Paths.Me)).StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, (await Replay(changed, cookie).GetAsync(Constants.Paths.Me)).StatusCode);
	}

	[Fact]
	public async Task Logout_in_one_instance_invalidates_the_cookie_after_a_restart()
	{
		using var first = new TessynWebFactory();
		using var browser = first.CreateBrowser();
		var cookie = CookieValue(await browser.PostAsJsonAsync(Constants.Paths.Login, Good), Constants.Auth.CookieName);
		await browser.PostAsync(Constants.Paths.Logout, null);

		using var restarted = new TessynWebFactory().SharingStateWith(first);

		Assert.Equal(HttpStatusCode.Unauthorized, (await Replay(restarted, cookie).GetAsync(Constants.Paths.Me)).StatusCode);
	}

	[Fact]
	public async Task The_login_ends_after_the_absolute_lifetime_even_when_used_continuously()
	{
		var time = new ManualTimeProvider();
		using var factory = new TessynWebFactory { Time = time };
		using var client = factory.CreateBrowser();
		await client.PostAsJsonAsync(Constants.Paths.Login, Good);

		for (var day = 10; day <= 28; day += 9)
		{
			time.Advance(TimeSpan.FromDays(9));
			Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Constants.Paths.Me)).StatusCode);
		}

		time.Advance(TimeSpan.FromDays(3));

		Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Constants.Paths.Me)).StatusCode);
	}

	[Fact]
	public async Task The_absolute_lifetime_is_configurable()
	{
		var time = new ManualTimeProvider();
		using var factory = new TessynWebFactory { Time = time }.Set("TessynWeb:Auth:AbsoluteDays", "3");
		using var client = factory.CreateBrowser();
		await client.PostAsJsonAsync(Constants.Paths.Login, Good);

		time.Advance(TimeSpan.FromDays(2));
		Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Constants.Paths.Me)).StatusCode);
		time.Advance(TimeSpan.FromDays(2));

		Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Constants.Paths.Me)).StatusCode);
	}

	[Fact]
	public async Task An_idle_login_expires_after_the_sliding_window()
	{
		var time = new ManualTimeProvider();
		using var factory = new TessynWebFactory { Time = time };
		using var client = factory.CreateBrowser();
		await client.PostAsJsonAsync(Constants.Paths.Login, Good);

		time.Advance(TimeSpan.FromDays(15));

		Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Constants.Paths.Me)).StatusCode);
	}

	[Fact]
	public async Task Parallel_wrong_guesses_from_one_ip_are_capped_at_the_per_ip_limit()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		var responses = await Task.WhenAll(Enumerable.Range(0, 40)
			.Select(i => Task.Run(() => client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "wrong" + i)))));

		Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
		Assert.Equal(35, responses.Count(r => (int)r.StatusCode == 429));
	}

	[Fact]
	public async Task A_successful_login_does_not_use_up_the_failure_budget()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();
		for (var i = 0; i < 4; i++)
			await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "wrong"));

		Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Constants.Paths.Login, Good)).StatusCode);
		for (var i = 0; i < 5; i++)
			Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "wrong"))).StatusCode);
	}

	[Fact]
	public async Task A_trusted_device_can_log_in_while_the_global_limit_is_exhausted()
	{
		using var factory = new TessynWebFactory()
			.Set("TessynWeb:Auth:Throttle:PerIpFailures", "1000")
			.Set("TessynWeb:Auth:Throttle:GlobalFailures", "3");
		using var owner = factory.CreateBrowser();
		var first = await owner.PostAsJsonAsync(Constants.Paths.Login, Good);
		var deviceCookie = CookieValue(first, Constants.Auth.DeviceCookieName);
		Assert.Contains("httponly", first.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(Constants.Auth.DeviceCookieName, StringComparison.Ordinal)), StringComparison.OrdinalIgnoreCase);

		using var attacker = Replay(factory);
		for (var i = 0; i < 3; i++)
			await attacker.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "guess" + i));
		Assert.Equal((HttpStatusCode)429, (await attacker.PostAsJsonAsync(Constants.Paths.Login, Good)).StatusCode);

		using var ownerAgain = Replay(factory, deviceCookie);
		Assert.Equal(HttpStatusCode.OK, (await ownerAgain.PostAsJsonAsync(Constants.Paths.Login, Good)).StatusCode);
	}

	[Fact]
	public async Task A_forged_or_foreign_device_cookie_gets_no_exemption()
	{
		using var factory = new TessynWebFactory()
			.Set("TessynWeb:Auth:Throttle:PerIpFailures", "1000")
			.Set("TessynWeb:Auth:Throttle:GlobalFailures", "2");
		using var attacker = Replay(factory, Constants.Auth.DeviceCookieName + "=CfDJ8AAAAforgedforgedforged");
		for (var i = 0; i < 2; i++)
			await attacker.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "guess" + i));

		Assert.Equal((HttpStatusCode)429, (await attacker.PostAsJsonAsync(Constants.Paths.Login, Good)).StatusCode);
	}

	[Fact]
	public async Task A_device_cookie_from_before_a_password_change_gets_no_exemption()
	{
		using var first = new TessynWebFactory();
		using var owner = first.CreateBrowser();
		var deviceCookie = CookieValue(await owner.PostAsJsonAsync(Constants.Paths.Login, Good), Constants.Auth.DeviceCookieName);

		using var changed = new TessynWebFactory().SharingStateWith(first)
			.Set("TessynWeb:Auth:PasswordHash", TessynWebFactory.HashFor("new password"))
			.Set("TessynWeb:Auth:Throttle:PerIpFailures", "1000")
			.Set("TessynWeb:Auth:Throttle:GlobalFailures", "2");
		using var client = Replay(changed, deviceCookie);
		for (var i = 0; i < 2; i++)
			await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "guess" + i));

		Assert.Equal((HttpStatusCode)429, (await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "new password"))).StatusCode);
	}

	[Fact]
	public async Task A_trusted_device_is_still_bound_by_the_per_ip_limit()
	{
		using var factory = new TessynWebFactory().Set("TessynWeb:Auth:Throttle:PerIpFailures", "2");
		using var owner = factory.CreateBrowser();
		var deviceCookie = CookieValue(await owner.PostAsJsonAsync(Constants.Paths.Login, Good), Constants.Auth.DeviceCookieName);
		using var device = Replay(factory, deviceCookie);
		for (var i = 0; i < 2; i++)
			await device.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(TessynWebFactory.Email, "guess" + i));

		Assert.Equal((HttpStatusCode)429, (await device.PostAsJsonAsync(Constants.Paths.Login, Good)).StatusCode);
	}

	[Fact]
	public async Task Login_bodies_over_4_KB_are_413_and_smaller_ones_are_parsed()
	{
		using var factory = new TessynWebFactory();
		using var client = factory.CreateBrowser();

		var big = await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(new string('a', 5000), "x"));
		var fine = await client.PostAsJsonAsync(Constants.Paths.Login, new LoginRequest(new string('a', 2000), "x"));

		Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, fine.StatusCode);
	}

	[Fact]
	public async Task Rpc_bodies_over_1_MB_are_413_even_before_authentication()
	{
		using var factory = new TessynWebFactory();
		using var anonymous = Replay(factory);
		var tooBig = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"client.hello\",\"params\":{\"x\":\"" + new string('a', 1024 * 1024 + 10) + "\"}}", System.Text.Encoding.UTF8, Constants.ContentTypes.Json);

		var response = await anonymous.PostAsync(Constants.Paths.Rpc, tooBig);

		Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
	}

	[Fact]
	public async Task Link_bodies_may_be_up_to_4_MB()
	{
		using var factory = new TessynWebFactory();
		using var daemon = factory.CreateDaemonHttp();
		daemon.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TessynWebFactory.LinkToken);

		var under = new StringContent("{\"events\":[],\"pad\":\"" + new string('a', 3 * 1024 * 1024) + "\"}", System.Text.Encoding.UTF8, Constants.ContentTypes.Json);
		var over = new StringContent("{\"events\":[],\"pad\":\"" + new string('a', 4 * 1024 * 1024 + 10) + "\"}", System.Text.Encoding.UTF8, Constants.ContentTypes.Json);

		Assert.Equal(HttpStatusCode.NoContent, (await daemon.PostAsync(Constants.Paths.LinkEvents, under)).StatusCode);
		Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await daemon.PostAsync(Constants.Paths.LinkEvents, over)).StatusCode);
		Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await daemon.PostAsync(Constants.Paths.LinkReply, over)).StatusCode);
	}

	[Fact]
	public void Body_limits_per_path()
	{
		Assert.Equal(4 * 1024, Hosting.RequestBodyLimitMiddleware.LimitFor(Constants.Paths.Login));
		Assert.Equal(1024 * 1024, Hosting.RequestBodyLimitMiddleware.LimitFor(Constants.Paths.Rpc));
		Assert.Equal(4 * 1024 * 1024, Hosting.RequestBodyLimitMiddleware.LimitFor(Constants.Paths.LinkReply));
		Assert.Equal(4 * 1024 * 1024, Hosting.RequestBodyLimitMiddleware.LimitFor(Constants.Paths.LinkEvents));
		Assert.Equal(Constants.Limits.MaxBodyBytes, Hosting.RequestBodyLimitMiddleware.LimitFor("/anything/else"));
	}
}
