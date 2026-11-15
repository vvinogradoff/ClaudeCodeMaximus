using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Re-check fixes: R1 Claude Code global state files, R2 canonical data root and user profile, R3 links that lead off the local
/// drives are never opened.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusRefsRecheckTests : IDisposable
{
	private readonly FocusRefsEnv _env = new();

	public void Dispose() => _env.Dispose();

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint length);

	private static bool TryCmd(string arguments)
	{
		using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + arguments) { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
		process.WaitForExit();
		return process.ExitCode == 0;
	}

	private static void Junction(string link, string target) => Assert.True(TryCmd($"mklink /J \"{link}\" \"{target}\""));

	private static FocusRefException Error(FocusRefResolver resolver, SessionNodeModel session, string path) =>
		Assert.Throws<FocusRefException>(() => resolver.Resolve(session, new FocusRefRequest("file", Path: path)));

	private AttentionRefModel Ok(FocusRefResolver resolver, string path) =>
		resolver.Resolve(_env.Session, new FocusRefRequest("file", Path: path));

	// ── R1 ───────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData(@"C:\Users\me\.claude.json")]
	[InlineData(@"C:\Users\me\.claude.json.backup")]
	[InlineData(@"C:\Users\me\.claude.json.corrupted.1731")]
	[InlineData(@"C:\Users\me\.CLAUDE.JSON")]
	[InlineData(@"C:\proj\.claude.json")]
	public void R1_claude_global_state_is_denied_on_both_transports(string path)
	{
		Assert.True(FileAccessPolicy.IsDenied(path, @"C:\Data\TessynDesktop"));
		Assert.True(FileAccessPolicy.IsDenied(path, @"C:\Data\TessynDesktop", relay: true));
	}

	[Theory]
	[InlineData(@"C:\Users\me\claude.json")]
	[InlineData(@"C:\Users\me\.claudeish.md")]
	public void R1_similar_names_are_not_caught(string path)
	{
		Assert.False(FileAccessPolicy.IsDenied(path, @"C:\Data\TessynDesktop", relay: true));
	}

	[Fact]
	public void R1_a_claude_json_cannot_be_referenced_by_its_name_or_its_8_3_name()
	{
		var full = Path.Combine(_env.UserProfile, ".claude.json");
		File.WriteAllText(full, "{\"mcpServers\":{}}");
		Assert.Contains("cannot be referenced", Error(_env.Resolver, _env.Session, full).Message);

		var buffer = new StringBuilder(260);
		var length = GetShortPathNameW(full, buffer, (uint)buffer.Capacity);
		var shortName = length > 0 ? buffer.ToString() : full;
		if (!string.Equals(shortName, full, StringComparison.OrdinalIgnoreCase))
			Assert.Contains("cannot be referenced", Error(_env.Resolver, _env.Session, shortName).Message);   // denied by the final path
	}

	// ── R2 ───────────────────────────────────────────────────────────────────

	[Fact]
	public void R2_the_canonical_form_of_a_junctioned_directory_is_added()
	{
		var real = Path.Combine(_env.Base.Root, "real-dir");
		Directory.CreateDirectory(real);
		var link = Path.Combine(_env.Base.Root, "link-dir");
		Junction(link, real);

		var forms = FileAccessPolicy.WithCanonical(link);

		Assert.Equal(2, forms.Length);
		Assert.Equal(link, forms[0]);
		Assert.Equal(real, forms[1], ignoreCase: true);
		Assert.Single(FileAccessPolicy.WithCanonical(real));
		Assert.Single(FileAccessPolicy.WithCanonical(Path.Combine(_env.Base.Root, "does-not-exist")));
	}

	[Fact]
	public void R2_data_root_rules_hold_when_the_configured_root_is_a_junction_and_the_real_path_is_named()
	{
		var real = Path.Combine(_env.Base.Root, "real-data");
		Directory.CreateDirectory(Path.Combine(real, "sessions"));
		File.WriteAllText(Path.Combine(real, "appsettings.json"), "{\"TdcpToken\":\"secret\"}");
		File.WriteAllText(Path.Combine(real, "sessions", "a.txt"), "transcript");
		var link = Path.Combine(_env.Base.Root, "link-data");
		Junction(link, real);
		var resolver = new FocusRefResolver(_env.Base.Registry, _env.Base.Store, link, _env.UserProfile);

		Assert.Contains("cannot be referenced", Error(resolver, _env.Session, Path.Combine(real, "appsettings.json")).Message);
		Assert.Contains("cannot be referenced", Error(resolver, _env.Session, Path.Combine(real, "sessions", "a.txt")).Message);
		Assert.Contains("cannot be referenced", Error(resolver, _env.Session, Path.Combine(link, "appsettings.json")).Message);
	}

	[Fact]
	public void R2_the_plans_exception_holds_for_a_user_profile_that_is_a_junction()
	{
		var realHome = Path.Combine(_env.Base.Root, "real-home");
		var plans = Path.Combine(realHome, ".claude", "plans");
		Directory.CreateDirectory(plans);
		File.WriteAllText(Path.Combine(plans, "p.md"), "# plan");
		File.WriteAllText(Path.Combine(plans, "p.json"), "{}");
		var linkHome = Path.Combine(_env.Base.Root, "link-home");
		Junction(linkHome, realHome);
		var resolver = new FocusRefResolver(_env.Base.Registry, _env.Base.Store, _env.DataRoot, linkHome);

		// Named by its real path (named = final): the canonical profile form makes the plans folder known.
		Assert.Equal("p.md", Ok(resolver, Path.Combine(plans, "p.md")).Name);
		Assert.Contains("cannot be referenced", Error(resolver, _env.Session, Path.Combine(plans, "p.json")).Message);
	}

	// ── R3 ───────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData(@"\\server\share\a.md", false)]
	[InlineData(@"//server/share/a.md", false)]
	[InlineData(@"\??\UNC\server\share\a.md", false)]
	[InlineData(@"\\?\C:\x\a.md", false)]
	[InlineData(@"\\.\C:\x\a.md", false)]
	[InlineData(@"\rooted\on\current\drive.md", false)]
	[InlineData(@"C:\x\a.md", true)]
	[InlineData(@"\??\C:\x\a.md", true)]
	[InlineData(@"..\other\a.md", true)]
	[InlineData("sibling.md", true)]
	[InlineData(@"C:x\a.md", false)]
	public void R3_only_plain_local_link_targets_are_accepted(string target, bool local)
	{
		Assert.Equal(local, SafeFilePath.TryLocalTarget(target, @"C:\proj\work", out _));
	}

	[Fact]
	public void R3_a_junction_to_a_local_directory_still_works()
	{
		var real = Path.Combine(_env.Base.Root, "local-real");
		Directory.CreateDirectory(real);
		File.WriteAllText(Path.Combine(real, "n.md"), "ok");
		Junction(Path.Combine(_env.WorkDir, "lj"), real);
		var full = Path.Combine(_env.WorkDir, "lj", "n.md");

		Assert.False(SafeFilePath.HasNonLocalLink(full));
		Assert.Equal("n.md", Ok(_env.Resolver, full).Name);
	}

	[Fact]
	public void R3_a_chain_of_links_that_ends_off_the_machine_or_is_too_long_is_refused()
	{
		// l0 -> l1 -> ... -> l9 -> real: longer than the depth limit, so refused even though every target is local.
		var real = Path.Combine(_env.Base.Root, "chain-real");
		Directory.CreateDirectory(real);
		File.WriteAllText(Path.Combine(real, "n.md"), "ok");
		var next = real;
		for (var i = 9; i >= 0; i--)
		{
			var link = Path.Combine(_env.Base.Root, $"chain{i}");
			Junction(link, next);
			next = link;
		}

		Assert.True(SafeFilePath.HasNonLocalLink(Path.Combine(next, "n.md")));
		Assert.Contains("cannot be referenced", Error(_env.Resolver, _env.Session, Path.Combine(next, "n.md")).Message);
	}

	[Fact]
	public void R3_a_file_symlink_to_a_unc_share_is_refused_without_being_opened()
	{
		var link = Path.Combine(_env.WorkDir, "unc.md");
		if (!TryCmd($"mklink \"{link}\" \"\\\\127.0.0.1\\nonexistent-share\\a.md\""))
			return;   // creating symlinks needs the privilege or Developer Mode; the target-text tests above cover the rule

		Assert.True(SafeFilePath.HasNonLocalLink(link));
		Assert.Null(SafeFilePath.Open(link));
		Assert.Contains("leads outside the local drives", Error(_env.Resolver, _env.Session, link).Message);
	}

	[Fact]
	public async Task R3_file_read_refuses_a_path_whose_parent_became_a_link_off_the_machine()
	{
		var folder = Path.Combine(_env.WorkDir, "docs");
		Directory.CreateDirectory(folder);
		File.WriteAllText(Path.Combine(folder, "a.md"), "x");
		await _env.PostAsync(FocusRefsEnv.ItemWithRefs("Doc", FocusRefsEnv.FileRef("docs/a.md")));
		var handle = _env.Attention.GetItems(_env.Session.NodeId).Single().Refs.Single().RefId!;
		Directory.Delete(folder, recursive: true);
		if (!TryCmd($"mklink /D \"{folder}\" \"\\\\127.0.0.1\\nonexistent-share\\docs\""))
			return;   // no symlink privilege

		var error = await _env.ErrorAsync("file.read", $"{{\"handle\":\"{handle}\"}}", TdcpTransport.Relay);

		Assert.Contains("leads outside the local drives", error);
	}
}
