using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// ISSUE.013 reproduction (decision A3, first step): a child pinned to profile P1, the parent then switched to P2, and a
/// headless <c>send_to_session</c> to the child (it runs through <see cref="SessionTurnService.RunTurnAsync(SessionNodeModel, string, TurnSource, CancellationToken)"/>).
/// These tests record what the code did BEFORE ISSUE.013 was fixed; see docs in the work report for the findings.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class Issue013ReproductionTests : IDisposable
{
	private readonly string _tempDir;
	private readonly AppSettingsService _appSettings;
	private readonly FakeProcessManagerForTurnService _processManager = new();
	private readonly FakeProfileServiceForTurnService _profileService = new();
	private readonly FakeFileServiceForTurnService _fileService = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly TagTestEnv _tagEnv = new();
	private readonly SessionTurnService _sut;

	public Issue013ReproductionTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"issue013-repro-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		_appSettings = new AppSettingsService(_tempDir);
		_appSettings.Settings.SessionFilesRoot = _tempDir;
		_appSettings.Settings.AgentToolsEnabled = false;
		_processManager.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "answer" });
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "sess-1" });
			return Task.CompletedTask;
		};

		_sut = new SessionTurnService(
			_processManager, _fileService, _appSettings, _profileService,
			new Lazy<IAgentMcpServer>(() => new FakeMcpServerForTurnService()),
			new FakeRegistryForTurnService(), new FakeSessionStatusServiceForTurnService(), new FakeModelServiceForTurnService(),
			new Lazy<ITelegramBridgeService>(() => new FakeBridgeForTurnService()),
			new FakeStreamingManagerForTurnService(), _attention.Service, _tagEnv.Service,
			new ProfileLabelService(_profileService, _appSettings));
	}

	public void Dispose()
	{
		_attention.Dispose();
		_tagEnv.Dispose();
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	private static SessionNodeModel Child(string profileName) =>
		new() { NodeId = "child-1", Name = "Child", FileName = "child-1.txt", WorkingDirectory = @"C:\proj", ProfileName = profileName, SupervisorNodeId = "parent-1" };

	[Fact]
	public async Task Reproduction_ChildPinnedToP1_StillRunsOnP1_AfterTheParentSwitchedToP2()
	{
		_appSettings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Account A" });
		_appSettings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p2", DisplayName = "Account B" });
		var parent = new SessionNodeModel { NodeId = "parent-1", Name = "Parent", FileName = "parent-1.txt", WorkingDirectory = @"C:\proj", ProfileName = "Account A" };
		parent.ProfileName = "Account B"; // the parent's switch: nothing touches its children

		var child = Child("Account A");
		var result = await _sut.RunTurnAsync(child, "continue", TurnSource.Orchestrated);

		// No error, no user step needed: the child resumes on its pinned profile (P1). The incident's "it fails" claim is not reproduced
		// while P1 still exists and is logged in.
		Assert.False(result.IsError);
		Assert.Equal(@"C:\profiles\p1", _processManager.Spawns.Single().profileConfigDir);
	}

	// Before the fix these two turns ran on Default with no error (the pre-fix commit records that). Since ISSUE.013 R2 they are errors.

	[Fact]
	public async Task ChildPinnedByNameToARenamedProfile_IsAnError_AndNeverRunsOnDefault()
	{
		_appSettings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Account A (renamed)" });

		var result = await _sut.RunTurnAsync(Child("Account A"), "continue", TurnSource.Orchestrated);

		Assert.True(result.IsError);
		Assert.Contains("'Account A'", result.ErrorMessage);
		Assert.Empty(_processManager.Spawns);
	}

	[Fact]
	public async Task ChildPinnedByIdSurvivesARename_AndStillRunsOnItsProfile()
	{
		_appSettings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Account A (renamed)" });
		var child = Child("Account A");
		child.ProfileId = "p1";

		var result = await _sut.RunTurnAsync(child, "continue", TurnSource.Orchestrated);

		Assert.False(result.IsError);
		Assert.Equal(@"C:\profiles\p1", _processManager.Spawns.Single().profileConfigDir);
	}

	[Fact]
	public async Task ChildPinnedToARemovedProfile_IsAnError_AndNeverRunsOnDefault()
	{
		var child = Child("Account A");
		child.ProfileId = "p1";

		var result = await _sut.RunTurnAsync(child, "continue", TurnSource.Orchestrated);

		Assert.True(result.IsError);
		Assert.Empty(_processManager.Spawns);
	}
}
