using System.Text.RegularExpressions;
using Tessyn.Web.Auth;

namespace Tessyn.Web.Tests;

/// <remarks>Created by Claude</remarks>
public sealed class PasswordHasherTests
{
	private readonly PasswordHasher _hasher = new();

	[Fact]
	public void Default_hash_has_the_documented_format()
	{
		var hash = _hasher.Hash("secret");

		var parts = hash.Split('$');
		Assert.Equal(4, parts.Length);
		Assert.Equal("pbkdf2-sha256", parts[0]);
		Assert.Equal("600000", parts[1]);
		Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
		Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
		Assert.Matches(new Regex(@"^pbkdf2-sha256\$\d+\$[A-Za-z0-9+/=]+\$[A-Za-z0-9+/=]+$"), hash);
	}

	[Fact]
	public void Hash_round_trips_and_rejects_other_passwords()
	{
		var hash = _hasher.Hash("secret", 1000);

		Assert.True(_hasher.Verify("secret", hash));
		Assert.False(_hasher.Verify("Secret", hash));
		Assert.False(_hasher.Verify(string.Empty, hash));
	}

	[Fact]
	public void Hashing_twice_uses_different_salts()
	{
		Assert.NotEqual(_hasher.Hash("secret", 1000), _hasher.Hash("secret", 1000));
	}

	[Theory]
	[InlineData("")]
	[InlineData("plaintext")]
	[InlineData("pbkdf2-sha256$abc$AAAA$AAAA")]
	[InlineData("pbkdf2-sha256$1000$not base64$AAAA")]
	[InlineData("pbkdf2-sha1$1000$AAAA$AAAA")]
	[InlineData("pbkdf2-sha256$0$AAAA$AAAA")]
	[InlineData("pbkdf2-sha256$1000$AAAA")]
	public void Malformed_hashes_never_verify(string stored)
	{
		Assert.False(_hasher.IsValidFormat(stored));
		Assert.False(_hasher.Verify("anything", stored));
	}
}
