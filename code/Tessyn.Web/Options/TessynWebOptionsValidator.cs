using Tessyn.Web.Auth;

namespace Tessyn.Web.Options;

/// <summary>Start-up validation of the TessynWeb config section; returns human-readable problems.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynWebOptionsValidator
{
	private readonly PasswordHasher _hasher;

	public TessynWebOptionsValidator(PasswordHasher hasher)
	{
		_hasher = hasher;
	}

	public IReadOnlyList<string> Validate(TessynWebOptions options)
	{
		var errors = new List<string>();
		var section = Constants.Config.Section;

		if (string.IsNullOrWhiteSpace(options.Auth.Email))
			errors.Add($"{section}:Auth:Email is not configured.");

		if (string.IsNullOrWhiteSpace(options.Auth.PasswordHash))
			errors.Add($"{section}:Auth:PasswordHash is not configured (generate one with '{Constants.Cli.HashPasswordVerb}').");
		else if (!_hasher.IsValidFormat(options.Auth.PasswordHash))
			errors.Add($"{section}:Auth:PasswordHash is not a valid {Constants.Pbkdf2.Prefix} hash.");

		if (string.IsNullOrWhiteSpace(options.Link.TokenSha256))
			errors.Add($"{section}:Link:TokenSha256 is not configured (generate a token with '{Constants.Cli.NewLinkTokenVerb}').");
		else if (!IsSha256Hex(options.Link.TokenSha256))
			errors.Add($"{section}:Link:TokenSha256 must be {Constants.Limits.Sha256HexLength} hex characters.");

		if (options.Auth.CookieDays < 1)
			errors.Add($"{section}:Auth:CookieDays must be at least 1.");

		if (options.Auth.AbsoluteDays < 1)
			errors.Add($"{section}:Auth:AbsoluteDays must be at least 1.");

		return errors;
	}

	private static bool IsSha256Hex(string value)
	{
		if (value.Length != Constants.Limits.Sha256HexLength)
			return false;

		foreach (var c in value)
		{
			if (!Uri.IsHexDigit(c))
				return false;
		}

		return true;
	}
}
