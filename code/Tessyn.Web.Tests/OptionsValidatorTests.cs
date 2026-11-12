using Tessyn.Web.Auth;
using Tessyn.Web.Options;

namespace Tessyn.Web.Tests;

/// <remarks>Created by Claude</remarks>
public sealed class OptionsValidatorTests
{
	private readonly PasswordHasher _hasher = new();

	private TessynWebOptions Valid()
	{
		return new TessynWebOptions
		{
			Auth = { Email = "a@b.test", PasswordHash = _hasher.Hash("x", 1000) },
			Link = { TokenSha256 = new string('a', 64) },
		};
	}

	[Fact]
	public void Valid_options_have_no_problems()
	{
		Assert.Empty(new TessynWebOptionsValidator(_hasher).Validate(Valid()));
	}

	[Fact]
	public void Missing_values_are_reported_clearly()
	{
		var problems = new TessynWebOptionsValidator(_hasher).Validate(new TessynWebOptions());

		Assert.Contains(problems, p => p.Contains("Auth:Email", StringComparison.Ordinal));
		Assert.Contains(problems, p => p.Contains("Auth:PasswordHash", StringComparison.Ordinal) && p.Contains("hash-password", StringComparison.Ordinal));
		Assert.Contains(problems, p => p.Contains("Link:TokenSha256", StringComparison.Ordinal) && p.Contains("new-link-token", StringComparison.Ordinal));
	}

	[Fact]
	public void Malformed_hash_and_token_are_reported()
	{
		var options = Valid();
		options.Auth.PasswordHash = "plaintext";
		options.Link.TokenSha256 = "xyz";

		var problems = new TessynWebOptionsValidator(_hasher).Validate(options);

		Assert.Equal(2, problems.Count);
	}
}
