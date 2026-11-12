using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Tessyn.Web.Auth;
using Tessyn.Web.Models;
using Tessyn.Web.Options;

namespace Tessyn.Web.Endpoints;

/// <summary>login / logout / me (contract section 7).</summary>
/// <remarks>Created by Claude</remarks>
public static class AuthEndpoints
{
	private const int MaxCredentialLength = 1024;

	public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
	{
		routes.MapPost(Constants.Paths.Login, (Delegate)LoginAsync);
		routes.MapPost(Constants.Paths.Logout, (Delegate)LogoutAsync);
		routes.MapGet(Constants.Paths.Me, Me).RequireAuthorization();
	}

	private static async Task<IResult> LoginAsync(
		HttpContext context,
		LoginRequest? request,
		IOptions<TessynWebOptions> options,
		PasswordHasher hasher,
		LoginThrottle throttle,
		TimeProvider time,
		ILoggerFactory loggerFactory)
	{
		var logger = loggerFactory.CreateLogger(Constants.Config.ServiceName + ".Auth");
		var auth = options.Value.Auth;
		var ip = context.Connection.RemoteIpAddress?.ToString() ?? Constants.Auth.UnknownClientIp;

		var retryAfter = throttle.GetRetryAfter(ip);
		if (retryAfter is not null)
		{
			logger.LogWarning("Login throttled for {Ip}", ip);
			context.Response.Headers[Constants.Auth.RetryAfterHeader] = ((int)Math.Ceiling(retryAfter.Value.TotalSeconds)).ToString();
			return Results.Json(new ErrorResponse(Constants.Auth.ThrottledMessage), statusCode: StatusCodes.Status429TooManyRequests);
		}

		var email = (request?.Email ?? string.Empty).Trim();
		var password = request?.Password ?? string.Empty;
		var withinLimits = email.Length <= MaxCredentialLength && password.Length <= MaxCredentialLength;

		// Always verify the password so an unknown email costs the same as a wrong password.
		var passwordOk = hasher.Verify(withinLimits ? password : string.Empty, auth.PasswordHash);
		var emailOk = string.Equals(email, auth.Email, StringComparison.OrdinalIgnoreCase);
		if (!(passwordOk && emailOk && withinLimits))
		{
			throttle.RecordFailure(ip);
			logger.LogWarning("Failed login from {Ip}", ip);
			return Results.Json(new ErrorResponse(Constants.Auth.InvalidCredentialsMessage), statusCode: StatusCodes.Status401Unauthorized);
		}

		throttle.RecordSuccess(ip);
		var identity = new ClaimsIdentity(
			[new Claim(ClaimTypes.Name, auth.Email), new Claim(ClaimTypes.Email, auth.Email)],
			Constants.Auth.CookieScheme);
		var properties = new AuthenticationProperties
		{
			IsPersistent = true,
			AllowRefresh = true,
			ExpiresUtc = time.GetUtcNow().AddDays(auth.CookieDays),
		};
		await context.SignInAsync(Constants.Auth.CookieScheme, new ClaimsPrincipal(identity), properties);
		return Results.Json(new EmailResponse(auth.Email));
	}

	private static async Task<IResult> LogoutAsync(HttpContext context)
	{
		await context.SignOutAsync(Constants.Auth.CookieScheme);
		return Results.NoContent();
	}

	private static IResult Me(HttpContext context)
	{
		var email = context.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
		return Results.Json(new EmailResponse(email));
	}
}
