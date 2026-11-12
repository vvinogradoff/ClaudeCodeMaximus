using System.Security.Cryptography;
using System.Text;
using Tessyn.Web.Auth;
using Tessyn.Web.Cli;

namespace Tessyn.Web.Tests;

/// <remarks>Created by Claude</remarks>
public sealed class CliTests
{
	private readonly PasswordHasher _hasher = new();

	private int? Run(string[] args, string stdin, out string stdout, out string stderr)
	{
		var outWriter = new StringWriter();
		var errWriter = new StringWriter();
		var code = new CliRunner(_hasher).TryRun(args, new StringReader(stdin), outWriter, errWriter, interactive: false);
		stdout = outWriter.ToString();
		stderr = errWriter.ToString();
		return code;
	}

	[Fact]
	public void No_verb_means_start_the_web_host()
	{
		Assert.Null(Run([], string.Empty, out _, out _));
		Assert.Null(Run(["--urls", "http://x"], string.Empty, out _, out _));
	}

	[Fact]
	public void Hash_password_reads_stdin_and_prints_a_verifiable_hash()
	{
		var code = Run([Constants.Cli.HashPasswordVerb], "my password\r\n", out var stdout, out _);

		Assert.Equal(0, code);
		var hash = stdout.Trim();
		Assert.StartsWith("pbkdf2-sha256$600000$", hash, StringComparison.Ordinal);
		Assert.True(_hasher.Verify("my password", hash));
	}

	[Fact]
	public void Hash_password_with_no_input_fails()
	{
		var code = Run([Constants.Cli.HashPasswordVerb], string.Empty, out var stdout, out var stderr);

		Assert.Equal(1, code);
		Assert.Empty(stdout);
		Assert.Contains(Constants.Cli.EmptyPasswordError, stderr, StringComparison.Ordinal);
	}

	[Fact]
	public void New_link_token_prints_a_base64url_token_and_its_sha256_hex()
	{
		var code = Run([Constants.Cli.NewLinkTokenVerb], string.Empty, out var stdout, out _);

		Assert.Equal(0, code);
		var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		Assert.Equal(2, lines.Length);
		var token = lines[0][Constants.Cli.TokenLabel.Length..];
		var sha = lines[1][Constants.Cli.TokenSha256Label.Length..];
		Assert.Equal(43, token.Length);
		Assert.Matches("^[A-Za-z0-9_-]+$", token);
		Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))), sha);
		Assert.Equal(64, sha.Length);
	}

	[Fact]
	public void New_link_tokens_are_unique()
	{
		Run([Constants.Cli.NewLinkTokenVerb], string.Empty, out var first, out _);
		Run([Constants.Cli.NewLinkTokenVerb], string.Empty, out var second, out _);

		Assert.NotEqual(first, second);
	}
}
