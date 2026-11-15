using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// J1 review T1, second part: in the WorkingDirectory file access mode (A11) a session whose working directory is too broad (a drive root,
/// the user profile or a parent of it, the data root) grants no relay reads, however the directory got into the tree. Referenced files and
/// attachments are not affected. The "profile" of the test environment is its own temp home folder.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class BroadWorkingDirectoryAccessTests : IDisposable
{
	private readonly FocusRefsEnv _env = new();

	public BroadWorkingDirectoryAccessTests()
	{
		_env.Base.Settings.Settings.FileAccess.Mode = FileAccessMode.WorkingDirectory;
	}

	public void Dispose() => _env.Dispose();

	private async Task<string> EntryIdOf(string quote)
	{
		var entries = await _env.ResultAsync("session.entries", _env.Base.NodeParams());
		return entries["entries"]!.AsArray().Last(e => e!["content"]!.GetValue<string>().Contains(quote))!["entryId"]!.GetValue<string>();
	}

	private string PreviewParams(string entryId, string path) =>
		new JsonObject { ["nodeId"] = _env.Session.NodeId, ["entryId"] = entryId, ["path"] = path }.ToJsonString();

	private string WriteUnder(string directory, string name)
	{
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, name);
		File.WriteAllText(path, "text");
		return path;
	}

	[Fact]
	public async Task AWorkingDirectoryThatIsTheProfileRoot_GrantsNoRelayRead_WhileANormalOneDoes()
	{
		var inProfileRoot = WriteUnder(_env.UserProfile, "notes.md");
		var inProject     = WriteUnder(Path.Combine(_env.UserProfile, "proj"), "plan.md");
		_env.WriteBodies("u", $"files {inProfileRoot} and {inProject}");
		var entryId = await EntryIdOf("files");

		_env.Session.WorkingDirectory = _env.UserProfile;
		var denied = await _env.ErrorAsync("file.preview", PreviewParams(entryId, inProfileRoot), TdcpTransport.Relay);

		_env.Session.WorkingDirectory = Path.Combine(_env.UserProfile, "proj");
		var allowed = await _env.ResultAsync("file.preview", PreviewParams(entryId, inProject), TdcpTransport.Relay);

		Assert.Contains("Only files a Focus item references", denied);
		Assert.Equal("plan.md", allowed["name"]!.GetValue<string>());
	}

	[Fact]
	public async Task AWorkingDirectoryThatIsAParentOfTheProfile_GrantsNoRelayRead()
	{
		var parent = Path.GetDirectoryName(_env.UserProfile)!;
		var file   = WriteUnder(parent, "shared.md");
		_env.WriteBodies("u", $"file {file}");
		var entryId = await EntryIdOf("file ");

		_env.Session.WorkingDirectory = parent;

		Assert.Contains("Only files a Focus item references", await _env.ErrorAsync("file.preview", PreviewParams(entryId, file), TdcpTransport.Relay));
	}

	[Fact]
	public async Task AReferencedFile_StillReadsOverTheRelay_WhenTheWorkingDirectoryIsTooBroad()
	{
		_env.WriteBodies("u", "x");
		_env.WriteFile("ref.md", "referenced");
		await _env.PostAsync(FocusRefsEnv.ItemWithRefs("File ref.md", FocusRefsEnv.FileRef("ref.md")));
		var handle = _env.Attention.GetItems(_env.Session.NodeId).First(i => i.Title == "File ref.md").Refs.Single().RefId!;

		_env.Session.WorkingDirectory = _env.UserProfile;
		var read = await _env.ResultAsync("file.read", new JsonObject { ["handle"] = handle }.ToJsonString(), TdcpTransport.Relay);

		Assert.Equal("referenced", read["data"]!.GetValue<string>());
	}

	[Fact]
	public async Task AnAttachment_StillReadsOverTheRelay_WhenTheWorkingDirectoryIsTooBroad()
	{
		var attachments = new AttachmentService(
			_env.Base.Registry, _env.Base.Settings, _ => null, new FakeBlobClient(), _env.DataRoot, _env.Clock, userProfile: _env.UserProfile);
		var files      = new FileAccessService(
			_env.Base.Registry, _env.Attention, _env.Base.Reader, _env.Base.Settings, _env.DataRoot, _env.Clock, userProfile: _env.UserProfile, attachments: attachments);
		var dispatcher = new TdcpDispatcher([new FileRpcModule(files)]);
		var source     = Path.Combine(_env.Base.Root, "source.md");
		File.WriteAllBytes(source, Encoding.UTF8.GetBytes("attached"));
		var stored = attachments.Import(_env.Session.NodeId, source);

		_env.Session.WorkingDirectory = _env.UserProfile;
		var request  = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "file.read", ["params"] = new JsonObject { ["handle"] = stored.AttachmentId } }.ToJsonString();
		var response = await dispatcher.HandleTextAsync(request, TdcpTransport.Relay, CancellationToken.None);

		Assert.Equal("attached", response!["result"]!["data"]!.GetValue<string>());
	}
}
