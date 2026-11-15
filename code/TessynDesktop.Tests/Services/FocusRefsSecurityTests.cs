using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Security review fixes of the Focus references (F1 to F9): the lexical path gate, the data-root and credential-folder deny rules,
/// the real path of the open file, the lifetime of a file ref, and the entry roles that can make a path "occur".
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusRefsSecurityTests : IDisposable
{
	private readonly FocusRefsEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static string ReadParams(string handle) => $"{{\"handle\":\"{handle}\"}}";

	private string PreviewParams(string entryId, string path) =>
		new System.Text.Json.Nodes.JsonObject { ["nodeId"] = _env.Session.NodeId, ["entryId"] = entryId, ["path"] = path }.ToJsonString();

	private FocusRefException ResolveError(string path) =>
		Assert.Throws<FocusRefException>(() => _env.Resolver.Resolve(_env.Session, new FocusRefRequest("file", Path: path)));

	private async Task<string> PostFileRef(string relative)
	{
		await _env.PostAsync(FocusRefsEnv.ItemWithRefs("File " + relative, FocusRefsEnv.FileRef(relative)));
		return _env.Attention.GetItems(_env.Session.NodeId).First(i => i.Title == "File " + relative).Refs.Single().RefId!;
	}

	/// <summary>Rewrites the stored path of the ref behind <paramref name="handle"/> (a ledger from an older version, or a tampered one) and restarts.</summary>
	private void Plant(string handle, string path)
	{
		var ledger = _env.LedgerStore.Load(_env.Session.NodeId);
		ledger.Items.SelectMany(i => i.Refs).Single(r => r.RefId == handle).AbsolutePath = path;
		_env.LedgerStore.Save(ledger);
		_env.Restart();
	}

	private static void Cmd(string arguments)
	{
		using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + arguments) { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
		process.WaitForExit();
		Assert.Equal(0, process.ExitCode);
	}

	// ── the lexical gate ─────────────────────────────────────────────────────

	[Theory]
	[InlineData(@"\\?\C:\Users\me\a.md")]
	[InlineData(@"\\.\C:\a.md")]
	[InlineData(@"\\.\PhysicalDrive0")]
	[InlineData(@"\\attacker.example\s\a.md")]
	[InlineData("//attacker.example/s/a.md")]
	[InlineData(@"\\?\UNC\attacker\s\a.md")]
	[InlineData(@"C:\a\b.txt:hidden")]
	[InlineData(@"C:\a\b.txt::$DATA")]
	[InlineData(@"C:\a\b.txt.")]
	[InlineData(@"C:\a\dir \b.txt")]
	[InlineData(@"C:\a\dir.\b.txt")]
	[InlineData(@"C:\a\CON")]
	[InlineData(@"C:\a\nul.txt")]
	[InlineData(@"C:\a\COM1.md")]
	[InlineData(@"C:\a\LPT9")]
	[InlineData("C:relative.md")]
	[InlineData(@"\no-drive.md")]
	[InlineData(@"C:\..\x.md")]
	[InlineData(@"C:\a\..\..\x.md")]
	[InlineData(@"C:\a\b?.md")]
	[InlineData("")]
	public void The_gate_rejects(string path)
	{
		Assert.False(SafeFilePath.TryNormalize(path, @"C:\proj", out _, out var reason));
		Assert.NotEmpty(reason);
	}

	[Theory]
	[InlineData(@"..\x.md")]
	[InlineData("a/../../x.md")]
	[InlineData("../../x.md")]
	public void A_relative_path_that_leaves_the_base_is_rejected(string path)
	{
		Assert.False(SafeFilePath.TryNormalize(path, @"C:\proj\work", out _, out var reason));
		Assert.Contains("working directory", reason);
	}

	[Theory]
	[InlineData(@"C:\a\b.txt", @"C:\a\b.txt")]
	[InlineData(@"c:/a/b.txt", @"C:\a\b.txt")]
	[InlineData(@"C:\a\.\b\..\c.md", @"C:\a\c.md")]
	[InlineData(@"C:\a\\b.txt", @"C:\a\b.txt")]
	[InlineData("docs/plan.md", @"C:\proj\work\docs\plan.md")]
	[InlineData(@"sub\..\x.md", @"C:\proj\work\x.md")]
	[InlineData("./x.md", @"C:\proj\work\x.md")]
	[InlineData("name.with.dots.md", @"C:\proj\work\name.with.dots.md")]
	public void The_gate_accepts_plain_drive_paths_and_normalises_them(string path, string expected)
	{
		Assert.True(SafeFilePath.TryNormalize(path, @"C:\proj\work", out var full, out _));
		Assert.Equal(expected, full);
	}

	[Fact]
	public void A_relative_path_needs_a_plain_base_directory()
	{
		Assert.False(SafeFilePath.TryNormalize("x.md", null, out _, out _));
		Assert.False(SafeFilePath.TryNormalize("x.md", @"\\server\share", out _, out _));
	}

	// ── F1: extended and device prefixes ─────────────────────────────────────

	[Theory]
	[InlineData(@"\\?\")]
	[InlineData(@"\\.\")]
	public void F1_an_extended_path_to_the_data_root_cannot_be_referenced(string prefix)
	{
		var secret = Path.Combine(_env.DataRoot, "appsettings.json");
		File.WriteAllText(secret, "{\"TdcpToken\":\"secret\"}");

		var error = ResolveError(prefix + secret);

		Assert.Contains("not allowed", error.Message);
	}

	[Fact]
	public async Task F1_a_planted_extended_path_is_refused_at_read_time_on_both_transports()
	{
		_env.WriteFile("ok.md", "fine");
		var handle = await PostFileRef("ok.md");
		var secret = Path.Combine(_env.DataRoot, "appsettings.json");
		File.WriteAllText(secret, "{\"TdcpToken\":\"secret\"}");
		Plant(handle, @"\\?\" + secret);

		Assert.Contains("not allowed", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay));
		Assert.Contains("not allowed", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Loopback));
	}

	[Fact]
	public async Task F1_preview_refuses_an_extended_path_even_when_the_assistant_wrote_it()
	{
		var secret = Path.Combine(_env.DataRoot, "mcp-tokens", "x.json");
		Directory.CreateDirectory(Path.GetDirectoryName(secret)!);
		File.WriteAllText(secret, "token");
		var spelled = @"\\?\" + secret;
		_env.WriteBodies("u", "look at " + spelled);
		var entryId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["entries"]![1]!["entryId"]!.GetValue<string>();

		Assert.Contains("not allowed", await _env.ErrorAsync("file.preview", PreviewParams(entryId, spelled), TdcpTransport.Relay));
		Assert.Contains("not allowed", await _env.ErrorAsync("file.preview", PreviewParams(entryId, spelled), TdcpTransport.Loopback));
	}

	// ── F2: UNC ──────────────────────────────────────────────────────────────

	[Fact]
	public async Task F2_a_unc_path_is_refused_by_name_before_any_filesystem_call()
	{
		const string unc = @"\\127.0.0.1\nonexistent-share\a.md";
		_env.WriteBodies("u", "see " + unc);
		var entryId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["entries"]![1]!["entryId"]!.GetValue<string>();

		// "File not found" would mean the path was probed; the gate must answer first.
		var preview = await _env.ErrorAsync("file.preview", PreviewParams(entryId, unc), TdcpTransport.Relay);
		Assert.Contains("UNC", preview);
		Assert.DoesNotContain("File not found", preview);
		Assert.Contains("UNC", ResolveError(unc).Message);
	}

	[Fact]
	public async Task F2_a_planted_unc_path_is_refused_without_being_opened()
	{
		_env.WriteFile("ok.md", "fine");
		var handle = await PostFileRef("ok.md");
		Plant(handle, @"\\127.0.0.1\nonexistent-share\a.md");

		var error = await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay);

		Assert.Contains("UNC", error);
		Assert.DoesNotContain("File not found", error);
	}

	// ── F3: the whole data root and credential folders ───────────────────────

	[Theory]
	[InlineData(@"attention\ledger.json")]
	[InlineData(@"sessions\a.txt")]
	[InlineData("models-cache.json")]
	[InlineData(@"logs\today.log")]
	[InlineData(@"entry-index\x.idx")]
	[InlineData(@"tags\linear-workspaces.json")]
	public void F3_a_file_of_the_data_root_cannot_be_referenced(string relative)
	{
		var full = Path.Combine(_env.DataRoot, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllText(full, "x");

		Assert.Contains("cannot be referenced", ResolveError(full).Message);
	}

	[Theory]
	[InlineData(@"C:\Data\TessynDesktop\sessions\a.txt", true)]
	[InlineData(@"C:\Data\TessynDesktop\attention\n.json", true)]
	[InlineData(@"C:\Data\TessynDesktop\models-cache.json", true)]
	[InlineData(@"C:\Data\TessynDesktop\attachments\n1\photo.png", false)]
	[InlineData(@"C:\Data\TessynDesktop\attachments\n1\photo.png.dat", true)]
	[InlineData(@"C:\Data\TessynDesktop\attachments", true)]
	[InlineData(@"C:\Data\TessynDesktop2\sessions\a.txt", false)]
	public void F3_the_relay_denies_the_data_root_except_attachments(string path, bool denied)
	{
		Assert.Equal(denied, FileAccessPolicy.IsDenied(path, @"C:\Data\TessynDesktop", relay: true));
	}

	[Theory]
	[InlineData(@"C:\Users\me\.config\gh\hosts.yml")]
	[InlineData(@"C:\Users\me\.config\gcloud\credentials.db")]
	[InlineData(@"C:\Users\me\.claude\settings.json")]
	[InlineData(@"C:\Users\me\.azure\accessTokens.json")]
	[InlineData(@"C:\Users\me\.aws\config")]
	[InlineData(@"C:\Users\me\.docker\config.json")]
	[InlineData(@"C:\Users\me\.kube\config")]
	[InlineData(@"C:\Users\me\AppData\Roaming\Microsoft\Credentials\ABC123")]
	[InlineData(@"C:\Users\me\AppData\Roaming\Microsoft\Protect\S-1-5\key")]
	[InlineData(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Login Data")]
	[InlineData(@"C:\Users\me\AppData\Local\Microsoft\Edge\User Data\Default\Cookies")]
	[InlineData(@"C:\Users\me\AppData\Roaming\Mozilla\Firefox\Profiles\x.default\logins.json")]
	[InlineData(@"C:\Users\me\AppData\Local\BraveSoftware\Brave-Browser\User Data\Default\Cookies")]
	public void F3_credential_locations_of_other_tools_are_denied_by_directory_for_both_transports(string path)
	{
		Assert.True(FileAccessPolicy.IsDenied(path, @"C:\Data\TessynDesktop"));
		Assert.True(FileAccessPolicy.IsDenied(path, @"C:\Data\TessynDesktop", relay: true));
	}

	[Theory]
	[InlineData(@"C:\Users\me\.config\other\x.md")]
	[InlineData(@"C:\Users\me\AppData\Roaming\Microsoft\Windows\x.md")]
	[InlineData(@"C:\proj\docs\plan.md")]
	public void F3_ordinary_folders_stay_readable(string path)
	{
		Assert.False(FileAccessPolicy.IsDenied(path, @"C:\Data\TessynDesktop", relay: true));
	}

	// ── F4 + F6: the real path of the open file ──────────────────────────────

	[Fact]
	public void F4_a_directory_junction_into_the_data_root_cannot_be_used_to_reference_a_secret()
	{
		var target = Path.Combine(_env.DataRoot, "mcp-tokens");
		Directory.CreateDirectory(target);
		File.WriteAllText(Path.Combine(target, "x.json"), "token");
		Cmd($"mklink /J \"{Path.Combine(_env.WorkDir, "lnk")}\" \"{target}\"");

		var error = ResolveError("lnk/x.json");

		Assert.Contains("cannot be referenced", error.Message);
	}

	[Fact]
	public async Task F4_a_junction_created_after_the_ref_does_not_open_the_secret_either()
	{
		var target = Path.Combine(_env.DataRoot, "mcp-tokens");
		Directory.CreateDirectory(target);
		File.WriteAllText(Path.Combine(target, "x.json"), "token");
		Directory.CreateDirectory(Path.Combine(_env.WorkDir, "swap"));
		_env.WriteFile("swap/x.json", "harmless");
		var handle = await PostFileRef("swap/x.json");

		// The harmless directory is replaced by a junction to the secret after the ref was made.
		Directory.Delete(Path.Combine(_env.WorkDir, "swap"), recursive: true);
		Cmd($"mklink /J \"{Path.Combine(_env.WorkDir, "swap")}\" \"{target}\"");

		Assert.Contains("cannot be read remotely", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay));
		Assert.Contains("cannot be read remotely", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Loopback));
	}

	[Fact]
	public void F4_a_hard_link_to_a_secret_cannot_be_referenced()
	{
		var secret = Path.Combine(_env.DataRoot, "appsettings.json");
		File.WriteAllText(secret, "{\"TdcpToken\":\"secret\"}");
		Cmd($"mklink /H \"{Path.Combine(_env.WorkDir, "innocent.md")}\" \"{secret}\"");

		var error = ResolveError("innocent.md");

		Assert.Contains("hard-linked", error.Message);
	}

	[Fact]
	public async Task F4_a_file_that_becomes_hard_linked_is_refused_on_the_relay()
	{
		var original = _env.WriteFile("doc.md", "text");
		var handle = await PostFileRef("doc.md");
		Cmd($"mklink /H \"{Path.Combine(_env.WorkDir, "alias.md")}\" \"{original}\"");

		Assert.Contains("hard-linked", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay));
		Assert.Equal("text", (await _env.ResultAsync("file.read", ReadParams(handle), TdcpTransport.Loopback))["data"]!.GetValue<string>());
	}

	[Fact]
	public async Task F6_the_policy_runs_on_the_final_path_so_a_junction_inside_the_working_directory_leaves_the_working_directory_mode()
	{
		var outside = Path.Combine(_env.Base.Root, "elsewhere");
		Directory.CreateDirectory(outside);
		File.WriteAllText(Path.Combine(outside, "n.md"), "x");
		Cmd($"mklink /J \"{Path.Combine(_env.WorkDir, "jn")}\" \"{outside}\"");
		var spelled = Path.Combine(_env.WorkDir, "jn", "n.md");
		_env.WriteBodies("u", "file " + spelled);
		var entryId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["entries"]![1]!["entryId"]!.GetValue<string>();
		_env.Base.Settings.Settings.FileAccess.Mode = FileAccessMode.WorkingDirectory;

		// Lexically inside the working directory, really outside it.
		Assert.Contains("Only files a Focus item references", await _env.ErrorAsync("file.preview", PreviewParams(entryId, spelled), TdcpTransport.Relay));
	}

	// ── F7: a ref grants access only while its item is open (plus a grace period) ──

	[Fact]
	public async Task F7_the_handle_dies_after_the_item_is_closed_and_the_grace_period_passed()
	{
		_env.WriteBodies("u", "x");
		_env.WriteFile("plan.md", "plan");
		var handle = await PostFileRef("plan.md");
		await _env.ResultAsync("file.read", ReadParams(handle));

		_env.Attention.ResolveBySession(_env.Session, "A1", AttentionStatus.Resolved, "done");

		await _env.ResultAsync("file.read", ReadParams(handle));   // within the grace period
		_env.Clock.Advance(Constants.FileAccess.RefGracePeriod + TimeSpan.FromSeconds(1));
		Assert.Contains("Unknown file handle", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay));
		Assert.Contains("Unknown file handle", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Loopback));
	}

	[Fact]
	public async Task F7_an_open_item_keeps_its_handle_for_as_long_as_it_is_open()
	{
		_env.WriteFile("plan.md", "plan");
		var handle = await PostFileRef("plan.md");

		_env.Clock.Advance(TimeSpan.FromDays(30));

		Assert.Equal("plan", (await _env.ResultAsync("file.read", ReadParams(handle)))["data"]!.GetValue<string>());
	}

	[Fact]
	public async Task F7_a_closed_item_no_longer_makes_its_file_a_referenced_path_for_preview()
	{
		_env.Base.Settings.Settings.FileAccess.Mode = FileAccessMode.ReferencedOnly;
		var full = _env.WriteFile("plan.md", "plan");
		_env.WriteBodies("u", "plan at " + full);
		await PostFileRef("plan.md");
		var entryId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["entries"]![1]!["entryId"]!.GetValue<string>();
		await _env.ResultAsync("file.preview", PreviewParams(entryId, full), TdcpTransport.Relay);

		_env.Attention.ResolveBySession(_env.Session, "A1", AttentionStatus.Answered, "ok");
		_env.Clock.Advance(Constants.FileAccess.RefGracePeriod + TimeSpan.FromSeconds(1));

		Assert.Contains("Only files a Focus item references", await _env.ErrorAsync("file.preview", PreviewParams(entryId, full), TdcpTransport.Relay));
	}

	// ── F5: only assistant entries make a path occur ─────────────────────────

	[Fact]
	public async Task F5_a_path_in_a_user_entry_does_not_count()
	{
		var full = _env.WriteFile("mine.md", "x");
		_env.WriteBodies(full, "an answer without the path");   // entry 0 is the user's
		var entries = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["entries"]!.AsArray();
		var userEntry = entries[0]!["entryId"]!.GetValue<string>();

		Assert.Contains("does not occur", await _env.ErrorAsync("file.preview", PreviewParams(userEntry, full), TdcpTransport.Loopback));
		Assert.Contains("does not occur", await _env.ErrorAsync("file.preview", PreviewParams(userEntry, full), TdcpTransport.Relay));
	}

	[Fact]
	public async Task F5_a_path_in_an_assistant_entry_still_counts()
	{
		var full = _env.WriteFile("theirs.md", "x");
		_env.WriteBodies("u", "wrote " + full);
		var assistant = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["entries"]![1]!["entryId"]!.GetValue<string>();

		await _env.ResultAsync("file.preview", PreviewParams(assistant, full), TdcpTransport.Loopback);
	}

	// ── F9: no active content ────────────────────────────────────────────────

	[Theory]
	[InlineData("a.svg")]
	[InlineData("a.html")]
	[InlineData("a.htm")]
	public void F9_svg_and_html_are_served_as_plain_text(string name)
	{
		var (kind, mime) = FileAccessPolicy.Classify(name);

		Assert.Equal(FileTypeClass.Text, kind);
		Assert.Equal("text/plain", mime);
	}

	[Fact]
	public async Task F9_an_svg_read_comes_back_as_utf8_text_plain()
	{
		_env.WriteFile("pic.svg", "<svg onload=\"alert(1)\"/>");
		var handle = await PostFileRef("pic.svg");

		var r = await _env.ResultAsync("file.read", ReadParams(handle), TdcpTransport.Relay);

		Assert.Equal("text/plain", r["mime"]!.GetValue<string>());
		Assert.Equal("utf8", r["encoding"]!.GetValue<string>());
	}
}
