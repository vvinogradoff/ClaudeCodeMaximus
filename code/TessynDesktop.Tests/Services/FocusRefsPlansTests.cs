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
/// The one exception to the credential-folder and data-root rules: a referenced, single-link <c>.md</c> file directly inside a plans
/// folder of a Claude config directory (<c>&lt;user profile&gt;\.claude\plans</c>, <c>&lt;data root&gt;\profiles\&lt;id&gt;\plans</c>) is readable,
/// because that is where plan mode writes the plans the user approves. Everything else in those folders stays denied.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusRefsPlansTests : IDisposable
{
	private readonly FocusRefsEnv _env = new();

	public void Dispose() => _env.Dispose();

	private string HomePlans => Path.Combine(_env.UserProfile, ".claude", "plans");
	private string ProfilePlans => Path.Combine(_env.DataRoot, "profiles", "work", "plans");

	private static string ReadParams(string handle) => $"{{\"handle\":\"{handle}\"}}";

	private string PreviewParams(string entryId, string path) =>
		new System.Text.Json.Nodes.JsonObject { ["nodeId"] = _env.Session.NodeId, ["entryId"] = entryId, ["path"] = path }.ToJsonString();

	private FocusRefException ResolveError(string path) =>
		Assert.Throws<FocusRefException>(() => _env.Resolver.Resolve(_env.Session, new FocusRefRequest("file", Path: path)));

	private static string WriteAt(string folder, string name, string text = "# plan")
	{
		Directory.CreateDirectory(folder);
		var full = Path.Combine(folder, name);
		File.WriteAllText(full, text);
		return full;
	}

	private async Task<string> RefAbsolute(string path)
	{
		await _env.PostAsync(FocusRefsEnv.ItemWithRefs("Plan " + Guid.NewGuid().ToString("N"), FocusRefsEnv.FileRef(path)));
		return _env.Attention.GetItems(_env.Session.NodeId).SelectMany(i => i.Refs).Last().RefId!;
	}

	private static void Cmd(string arguments)
	{
		using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + arguments) { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
		process.WaitForExit();
		Assert.Equal(0, process.ExitCode);
	}

	private static void Junction(string link, string target) => Cmd($"mklink /J \"{link}\" \"{target}\"");

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task A_referenced_plan_md_is_readable_on_both_transports(bool userHome)
	{
		var plan = WriteAt(userHome ? HomePlans : ProfilePlans, "my-plan.md", "# Approve me");

		var handle = await RefAbsolute(plan);

		foreach (var transport in new[] { TdcpTransport.Relay, TdcpTransport.Loopback })
			Assert.Equal("# Approve me", (await _env.ResultAsync("file.read", ReadParams(handle), transport))["data"]!.GetValue<string>());
	}

	[Fact]
	public void Other_files_in_those_folders_stay_denied()
	{
		foreach (var name in new[] { "x.json", ".credentials.json", "settings.json", "plan.txt", "credentials.md" })
		{
			Assert.Contains("cannot be referenced", ResolveError(WriteAt(HomePlans, name)).Message);
			Assert.Contains("cannot be referenced", ResolveError(WriteAt(ProfilePlans, name)).Message);
		}

		Assert.Contains("cannot be referenced", ResolveError(WriteAt(Path.Combine(_env.UserProfile, ".claude"), "notes.md")).Message);
		Assert.Contains("cannot be referenced", ResolveError(WriteAt(Path.Combine(_env.DataRoot, "profiles", "work"), "notes.md")).Message);
		Assert.Contains("cannot be referenced", ResolveError(WriteAt(Path.Combine(_env.UserProfile, ".claude", "projects", "p"), "s.md")).Message);
	}

	[Fact]
	public void A_file_in_a_subfolder_of_plans_is_denied()
	{
		Assert.Contains("cannot be referenced", ResolveError(WriteAt(Path.Combine(HomePlans, "sub"), "p.md")).Message);
		Assert.Contains("cannot be referenced", ResolveError(WriteAt(Path.Combine(ProfilePlans, "sub"), "p.md")).Message);
		Assert.Contains("cannot be referenced", ResolveError(WriteAt(Path.Combine(_env.DataRoot, "profiles", "work", "other"), "p.md")).Message);
		Assert.Contains("cannot be referenced", ResolveError(WriteAt(Path.Combine(_env.DataRoot, "profiles", "plans"), "p.md")).Message);
	}

	[Fact]
	public void A_junction_named_plans_is_denied()
	{
		var real = HomePlans;
		WriteAt(real, "p.md");
		Junction(Path.Combine(_env.WorkDir, "plans"), real);

		Assert.Contains("cannot be referenced", ResolveError("plans/p.md").Message);
	}

	[Fact]
	public void A_plans_path_reached_through_a_junction_is_denied()
	{
		WriteAt(HomePlans, "p.md");
		Junction(Path.Combine(_env.WorkDir, "cl"), Path.Combine(_env.UserProfile, ".claude"));

		Assert.Contains("cannot be referenced", ResolveError(Path.Combine(_env.WorkDir, "cl", "plans", "p.md")).Message);
	}

	[Fact]
	public async Task A_junction_swapped_in_after_the_ref_is_denied_at_read_time()
	{
		var elsewhere = Path.Combine(_env.Base.Root, "elsewhere-plans");
		WriteAt(elsewhere, "p.md");
		var folder = HomePlans;
		WriteAt(folder, "p.md", "real");
		var handle = await RefAbsolute(Path.Combine(folder, "p.md"));

		Directory.Delete(folder, recursive: true);
		Junction(folder, elsewhere);

		// The named path is unchanged but the real path is elsewhere: the exception no longer holds and the folder rule applies.
		Assert.Contains("cannot be read remotely", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay));
		Assert.Contains("cannot be read remotely", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Loopback));
	}

	[Fact]
	public async Task A_hard_linked_plan_is_denied()
	{
		var plan = WriteAt(HomePlans, "p.md");
		var handle = await RefAbsolute(plan);
		Cmd($"mklink /H \"{Path.Combine(_env.WorkDir, "alias.md")}\" \"{plan}\"");

		Assert.Contains("cannot be read remotely", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay));
		Assert.Contains("cannot be read remotely", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Loopback));
	}

	[Fact]
	public async Task An_unreferenced_plan_stays_denied_for_preview_until_it_is_referenced()
	{
		var plan = WriteAt(HomePlans, "p.md");
		_env.WriteBodies("u", "plan at " + plan);
		var entryId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["entries"]![1]!["entryId"]!.GetValue<string>();

		Assert.Contains("cannot be read remotely", await _env.ErrorAsync("file.preview", PreviewParams(entryId, plan), TdcpTransport.Loopback));
		Assert.Contains("cannot be read remotely", await _env.ErrorAsync("file.preview", PreviewParams(entryId, plan), TdcpTransport.Relay));

		await RefAbsolute(plan);
		await _env.ResultAsync("file.preview", PreviewParams(entryId, plan), TdcpTransport.Loopback);
		await _env.ResultAsync("file.preview", PreviewParams(entryId, plan), TdcpTransport.Relay);
	}

	[Theory]
	[InlineData(@"C:\Users\me\.claude\plans\a.md", true)]
	[InlineData(@"C:\Users\me\.claude\plans\a.MD", true)]
	[InlineData(@"C:\Users\me\.claude\plans\a.json", false)]
	[InlineData(@"C:\Users\me\.claude\plans\sub\a.md", false)]
	[InlineData(@"C:\Users\me\.claude\a.md", false)]
	[InlineData(@"C:\Users\you\.claude\plans\a.md", false)]
	[InlineData(@"C:\Data\TessynDesktop\profiles\work\plans\a.md", true)]
	[InlineData(@"C:\Data\TessynDesktop\profiles\work\plans\sub\a.md", false)]
	[InlineData(@"C:\Data\TessynDesktop\profiles\plans\a.md", false)]
	[InlineData(@"C:\Data\TessynDesktop\plans\a.md", false)]
	public void The_exact_locations(string path, bool isPlan)
	{
		Assert.Equal(isPlan, FileAccessPolicy.IsPlanFile(path, @"C:\Data\TessynDesktop", @"C:\Users\me"));
	}
}
