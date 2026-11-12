using System.Security.Cryptography;

namespace Tessyn.Web.Auth;

/// <summary>PBKDF2-SHA256 password hashing in the format pbkdf2-sha256$iterations$base64salt$base64hash.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PasswordHasher
{
	public string Hash(string password)
	{
		return Hash(password, Constants.Pbkdf2.Iterations);
	}

	public string Hash(string password, int iterations)
	{
		var salt = RandomNumberGenerator.GetBytes(Constants.Pbkdf2.SaltBytes);
		var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, Constants.Pbkdf2.HashBytes);
		var sep = Constants.Pbkdf2.Separator;
		return $"{Constants.Pbkdf2.Prefix}{sep}{iterations}{sep}{Convert.ToBase64String(salt)}{sep}{Convert.ToBase64String(hash)}";
	}

	public bool Verify(string password, string stored)
	{
		if (!TryParse(stored, out var iterations, out var salt, out var expected))
			return false;

		var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
		return CryptographicOperations.FixedTimeEquals(actual, expected);
	}

	public bool IsValidFormat(string stored)
	{
		return TryParse(stored, out _, out _, out _);
	}

	private static bool TryParse(string stored, out int iterations, out byte[] salt, out byte[] hash)
	{
		iterations = 0;
		salt = [];
		hash = [];

		var parts = stored.Split(Constants.Pbkdf2.Separator);
		if (parts.Length != Constants.Pbkdf2.PartCount || parts[0] != Constants.Pbkdf2.Prefix)
			return false;

		if (!int.TryParse(parts[1], out iterations) || iterations < Constants.Pbkdf2.MinIterations || iterations > Constants.Pbkdf2.MaxIterations)
			return false;

		try
		{
			salt = Convert.FromBase64String(parts[2]);
			hash = Convert.FromBase64String(parts[3]);
		}
		catch (FormatException)
		{
			return false;
		}

		return salt.Length > 0 && hash.Length > 0;
	}
}
