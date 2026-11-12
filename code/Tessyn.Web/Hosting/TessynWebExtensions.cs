using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Tessyn.Web.Auth;
using Tessyn.Web.Link;
using Tessyn.Web.Options;

namespace Tessyn.Web.Hosting;

/// <summary>Service registration and middleware pipeline for Tessyn.Web.</summary>
/// <remarks>Created by Claude</remarks>
public static class TessynWebExtensions
{
	private const string WebManifestExtension = ".webmanifest";
	private const string WebManifestContentType = "application/manifest+json";

	public static WebApplicationBuilder AddTessynWeb(this WebApplicationBuilder builder)
	{
		var services = builder.Services;

		builder.WebHost.ConfigureKestrel(kestrel =>
		{
			kestrel.AddServerHeader = false;
			kestrel.Limits.MaxRequestBodySize = Constants.Limits.MaxBodyBytes;
		});

		services.AddOptions<TessynWebOptions>().Bind(builder.Configuration.GetSection(Constants.Config.Section));

		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<WireJson>();
		services.AddSingleton<RemoteMethodAllowlist>();
		services.AddSingleton<DaemonLinkHub>();
		services.AddSingleton<SseWriter>();
		services.AddSingleton<PasswordHasher>();
		services.AddSingleton<LoginThrottle>();
		services.AddSingleton<AuthEpochStore>();
		services.AddSingleton<AuthSessionService>();
		services.AddSingleton<TrustedDevice>();
		services.AddSingleton<LinkTokenValidator>();
		services.AddSingleton<TessynWebOptionsValidator>();
		services.AddSingleton(sp => new SpaFiles(sp.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider));

		services.ConfigureHttpJsonOptions(options =>
		{
			var wire = new WireJson().Options;
			options.SerializerOptions.DefaultIgnoreCondition = wire.DefaultIgnoreCondition;
			options.SerializerOptions.PropertyNameCaseInsensitive = true;
			foreach (var converter in wire.Converters)
				options.SerializerOptions.Converters.Add(converter);
		});

		services.AddDataProtection().SetApplicationName(Constants.Config.ServiceName);
		services.AddSingleton<IConfigureOptions<KeyManagementOptions>, DataProtectionKeysConfigurator>();

		services.AddAuthentication(Constants.Auth.CookieScheme)
			.AddCookie(Constants.Auth.CookieScheme, cookie =>
			{
				cookie.Cookie.Name = Constants.Auth.CookieName;
				cookie.Cookie.HttpOnly = true;
				cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
				cookie.Cookie.SameSite = SameSiteMode.Strict;
				cookie.Cookie.Path = "/";
				cookie.SlidingExpiration = true;
				cookie.Events.OnValidatePrincipal = async validate =>
				{
					var session = validate.HttpContext.RequestServices.GetRequiredService<AuthSessionService>();
					if (!session.IsValid(validate.Principal))
					{
						validate.RejectPrincipal();
						await validate.HttpContext.SignOutAsync(Constants.Auth.CookieScheme);
					}
				};
				cookie.Events.OnRedirectToLogin = context =>
				{
					context.Response.StatusCode = StatusCodes.Status401Unauthorized;
					return Task.CompletedTask;
				};
				cookie.Events.OnRedirectToAccessDenied = context =>
				{
					context.Response.StatusCode = StatusCodes.Status403Forbidden;
					return Task.CompletedTask;
				};
			});
		services.AddOptions<CookieAuthenticationOptions>(Constants.Auth.CookieScheme)
			.Configure<IOptions<TessynWebOptions>, TimeProvider>((cookie, options, time) =>
			{
				cookie.TimeProvider = time;
				cookie.ExpireTimeSpan = TimeSpan.FromDays(options.Value.Auth.CookieDays);
			});
		services.AddAuthorization();

		services.AddResponseCompression(compression =>
		{
			compression.EnableForHttps = true;
			compression.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append("image/svg+xml");
		});

		services.Configure<ForwardedHeadersOptions>(forwarded =>
		{
			forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
			forwarded.ForwardLimit = 1;
			forwarded.KnownProxies.Add(IPAddress.Loopback);
			forwarded.KnownProxies.Add(IPAddress.IPv6Loopback);
		});

		return builder;
	}

	public static WebApplication UseTessynWeb(this WebApplication app)
	{
		app.UseForwardedHeaders();
		app.UseMiddleware<RequestBodyLimitMiddleware>();
		app.UseResponseCompression();
		app.UseMiddleware<SecurityHeadersMiddleware>();

		var contentTypes = new FileExtensionContentTypeProvider();
		contentTypes.Mappings[WebManifestExtension] = WebManifestContentType;
		var spa = app.Services.GetRequiredService<SpaFiles>();
		app.UseStaticFiles(new StaticFileOptions
		{
			FileProvider = spa.Provider,
			ContentTypeProvider = contentTypes,
			OnPrepareResponse = prepared =>
			{
				var immutable = prepared.Context.Request.Path.StartsWithSegments(Constants.Paths.AssetsPrefix.TrimEnd('/'));
				prepared.Context.Response.Headers.CacheControl = immutable
					? Constants.HeaderValues.ImmutableAssets
					: Constants.HeaderValues.NoCache;
			},
		});

		app.UseMiddleware<ClientHeaderMiddleware>();
		app.UseAuthentication();
		app.UseAuthorization();
		return app;
	}
}
