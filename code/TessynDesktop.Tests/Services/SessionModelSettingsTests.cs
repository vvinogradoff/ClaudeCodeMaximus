using System;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// TC.ME.01 (the rule) and the <see cref="SessionModelSettings.RecordPicks"/> part of TC.ME.02 (sticky picks): the session's model and effort
/// over a real <see cref="SessionRegistry"/> and settings in a temp dir — FR.12.35.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionModelSettingsTests : IDisposable
{
	private const string Opus   = "claude-opus-5-5";
	private const string Sonnet = "claude-sonnet-5-5";

	private readonly string _tempDir;
	private readonly AppSettingsService _settings;
	private readonly SessionRegistry _registry;
	private readonly DirectoryNodeModel _dir;
	private readonly SessionNodeModel _node;
	private readonly SessionModelSettings _sut;

	public SessionModelSettingsTests()
	{
		_tempDir  = Path.Combine(Path.GetTempPath(), "cm_test_model_settings_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		_settings = new AppSettingsService(_tempDir);
		_registry = new SessionRegistry(_settings, new FakeSessionFileService(), new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		_dir      = _registry.AddDirectory(Path.Combine(_tempDir, "proj"));
		_node     = _registry.AddSession(_dir, "S");
		_sut      = new SessionModelSettings(_settings);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private SessionModelSettingsModel Turn(string? model = null, string? effort = null) =>
		_sut.ResolveTurn(_node, model == null && effort == null ? null : new TurnOptionsModel(model, effort));

	// ── TC.ME.01 — the rule ─────────────────────────────────────────────────────

	[Fact]
	public void Model_PickWins_DefaultPickMeansNoFlag()
	{
		_node.ModelId = Opus;

		Assert.Equal(Sonnet, Turn(model: Sonnet).ModelId);
		Assert.Null(Turn(model: "default").ModelId);
		Assert.Null(Turn(model: "DEFAULT").ModelId);
	}

	[Fact]
	public void Model_NoPick_SessionThenDirectoryThenApp()
	{
		Assert.Null(Turn().ModelId);

		_settings.Settings.SelectedModelId = "app-model";
		Assert.Equal("app-model", Turn().ModelId);

		_dir.SelectedModelId = "dir-model";
		Assert.Equal("dir-model", Turn().ModelId);

		_node.ModelId = "node-model";
		Assert.Equal("node-model", Turn().ModelId);
	}

	[Fact]
	public void Model_SessionDefault_NoFlagAndNoFallback()
	{
		_dir.SelectedModelId = "dir-model";
		_settings.Settings.SelectedModelId = "app-model";
		_node.ModelId = "default";

		Assert.Null(Turn().ModelId);
		Assert.Null(_sut.ResolveSession(_node).ModelId);
	}

	[Fact]
	public void Effort_PickWins_ElseSession_ElseNone()
	{
		Assert.Null(Turn().Effort);

		_node.Effort = "max";
		Assert.Equal("max", Turn().Effort);
		Assert.Equal("high", Turn(effort: "high").Effort);
		Assert.Equal("max", Turn(effort: "").Effort);   // a blank pick is no pick
	}

	[Fact]
	public void Effort_DefaultMeansNone()
	{
		_node.Effort = "max";
		Assert.Null(Turn(effort: "default").Effort);

		_node.Effort = "default";
		Assert.Null(Turn().Effort);
	}

	[Fact]
	public void Session_IsWhatATurnWithoutPickUses_DefaultWhenNone()
	{
		Assert.Equal(new SessionModelSettingsModel(null, null), _sut.ResolveSession(_node));

		_dir.SelectedModelId = Opus;
		_node.Effort = "low";
		Assert.Equal(new SessionModelSettingsModel(Opus, "low"), _sut.ResolveSession(_node));
		Assert.Equal(_sut.ResolveSession(_node), Turn());
	}

	// ── TC.ME.02 — sticky picks ─────────────────────────────────────────────────

	[Fact]
	public void RecordPicks_DifferentPick_StoredAndSaved()
	{
		Assert.True(_sut.RecordPicks(_node, new TurnOptionsModel(Sonnet, "max")));

		Assert.Equal((Sonnet, "max"), (_node.ModelId, _node.Effort));
		var fromDisk = new AppSettingsService(_tempDir);
		fromDisk.Load();
		var reloaded = fromDisk.Settings.Tree.Single().Sessions.Single(s => s.NodeId == _node.NodeId);
		Assert.Equal((Sonnet, "max"), (reloaded.ModelId, reloaded.Effort));
	}

	[Fact]
	public void RecordPicks_EqualToCurrent_NothingChanged()
	{
		_dir.SelectedModelId = Opus;

		Assert.False(_sut.RecordPicks(_node, new TurnOptionsModel(Opus, "default")));

		Assert.Null(_node.ModelId);   // still inherits: a client that sends what it shows pins nothing
		Assert.Null(_node.Effort);
	}

	[Fact]
	public void RecordPicks_DefaultModelOverInheritedModel_StoresDefault()
	{
		_dir.SelectedModelId = Opus;
		_node.Effort = "max";

		Assert.True(_sut.RecordPicks(_node, new TurnOptionsModel("default", "default")));

		Assert.Equal("default", _node.ModelId);   // null would bring the directory's model back
		Assert.Null(_node.Effort);
		Assert.Equal(new SessionModelSettingsModel(null, null), _sut.ResolveSession(_node));
	}

	[Fact]
	public void RecordPicks_NoOptions_NothingChanged()
	{
		_node.ModelId = Opus;

		Assert.False(_sut.RecordPicks(_node, null));
		Assert.False(_sut.RecordPicks(_node, new TurnOptionsModel(ProfileName: "Work")));

		Assert.Equal(Opus, _node.ModelId);
		Assert.Null(_node.Effort);
	}

	// ── What a client is shown, and picks compared with it (M1, L3 of the review) ──

	private SessionModelSettings WithCatalog() => new(_settings, new ScriptedModelService());   // claude-opus-5-5, claude-sonnet-5-5

	[Fact]
	public void ResolveShown_UnlistedModelIsDefault_ListedMatchedCaseInsensitively()
	{
		var sut = WithCatalog();

		_node.ModelId = "CLAUDE-OPUS-5-5";
		Assert.Equal(Opus, sut.ResolveShown(_node).ModelId);

		_node.ModelId = "claude-retired-1";
		Assert.Null(sut.ResolveShown(_node).ModelId);
		Assert.Equal("claude-retired-1", sut.ResolveSession(_node).ModelId);   // what runs is unchanged
	}

	[Fact]
	public void RecordPicks_DefaultPickForAnUnlistedModel_ChangesNothing()
	{
		var sut = WithCatalog();
		_node.ModelId = "claude-retired-1";

		Assert.False(sut.RecordPicks(_node, new TurnOptionsModel("default")));

		Assert.Equal("claude-retired-1", _node.ModelId);   // a client sending back the "default" it was shown resets nothing
	}

	[Fact]
	public void RecordPicks_ComparesInCatalogForm_AndStoresIt()
	{
		var sut = WithCatalog();
		_node.ModelId = "CLAUDE-OPUS-5-5";

		Assert.False(sut.RecordPicks(_node, new TurnOptionsModel(Opus)));
		Assert.True(sut.RecordPicks(_node, new TurnOptionsModel("CLAUDE-SONNET-5-5")));

		Assert.Equal(Sonnet, _node.ModelId);
	}
}
