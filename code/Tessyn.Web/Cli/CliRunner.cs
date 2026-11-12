using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Tessyn.Web.Auth;

namespace Tessyn.Web.Cli;

/// <summary>Command-line verbs handled before the web host starts: hash-password and new-link-token.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CliRunner
{
	private readonly PasswordHasher _hasher;

	public CliRunner(PasswordHasher hasher)
	{
		_hasher = hasher;
	}

	/// <summary>Returns the process exit code when args name a verb, or null when the web host should start.</summary>
	public int? TryRun(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr, bool interactive)
	{
		if (args.Length == 0)
			return null;

		return args[0] switch
		{
			Constants.Cli.HashPasswordVerb => HashPassword(stdin, stdout, stderr, interactive),
			Constants.Cli.NewLinkTokenVerb => NewLinkToken(stdout),
			_ => null,
		};
	}

	private int HashPassword(TextReader stdin, TextWriter stdout, TextWriter stderr, bool interactive)
	{
		if (interactive)
			stderr.Write(Constants.Cli.PasswordPrompt);

		var password = stdin.ReadLine()?.TrimEnd('\r', '\n');
		if (string.IsNullOrEmpty(password))
		{
			stderr.WriteLine(Constants.Cli.EmptyPasswordError);
			return 1;
		}

		stdout.WriteLine(_hasher.Hash(password));
		return 0;
	}

	private static int NewLinkToken(TextWriter stdout)
	{
		var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Constants.Limits.TokenBytes));
		var sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
		stdout.WriteLine(Constants.Cli.TokenLabel + token);
		stdout.WriteLine(Constants.Cli.TokenSha256Label + sha256);
		return 0;
	}
}
