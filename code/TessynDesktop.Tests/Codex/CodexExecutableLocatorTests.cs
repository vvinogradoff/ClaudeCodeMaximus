using System;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Tests.Codex.Fakes;

namespace TessynDesktop.Tests.Codex;

/// <summary><see cref="CodexExecutableLocator"/>: the discovery order of FR.33.17 and the version check of FR.33.6 (TC.CX.36).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexExecutableLocatorTests
{
	private const string LocalAppData = @"C:\Users\u\AppData\Local";
	private const string AppData      = @"C:\Users\u\AppData\Roaming";
	private const string Installer    = LocalAppData + @"\Programs\OpenAI\Codex\bin\codex.exe";
	private const string WingetLink   = LocalAppData + @"\Microsoft\WinGet\Links\codex.exe";
	private const string WingetTarget = LocalAppData + @"\Microsoft\WinGet\Packages\OpenAI.Codex_Microsoft.Winget.Source_8wekyb3d8bbwe\codex-x86_64-pc-windows-msvc.exe";
	private const string NpmFolder    = AppData + @"\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64";
	private const string NpmNative    = NpmFolder + @"\vendor\x86_64-pc-windows-msvc\codex\codex.exe";
	private const string NpmShim      = AppData + @"\npm\codex.cmd";

	private readonly FakeCodexLocatorEnvironment _env = new();
	private readonly CodexExecutableLocator      _locator;

	public CodexExecutableLocatorTests()
	{
		_env.Variables["LOCALAPPDATA"] = LocalAppData;
		_env.Variables["APPDATA"]      = AppData;
		_env.Variables["PATH"]         = AppData + @"\npm;C:\Tools;C:\Windows";
		_locator = new CodexExecutableLocator(_env);
	}

	[Fact]
	public void OfficialInstaller_ComesFirst()
	{
		_env.Files.UnionWith([Installer, WingetLink, WingetTarget, NpmNative]);

		var result = _locator.Locate(null);

		Assert.Equal(CodexExecutableSource.OfficialInstaller, result.Source);
		Assert.Equal(Installer, result.ExecutablePath);
	}

	[Fact]
	public void WingetLink_IsResolvedToTheNativeExeItPointsTo()
	{
		_env.Files.UnionWith([WingetLink, WingetTarget, NpmNative]);
		_env.Links[WingetLink] = WingetTarget;

		var result = _locator.Locate(null);

		Assert.Equal(CodexExecutableSource.Winget, result.Source);
		Assert.Equal(WingetTarget, result.ExecutablePath);
		Assert.Equal(WingetLink, result.DiscoveredPath);
	}

	[Fact]
	public void Npm_UsesTheNativeExe_NeverTheCmdShim()
	{
		_env.Files.UnionWith([NpmShim, NpmNative]);

		var result = _locator.Locate(null);

		Assert.Equal(CodexExecutableSource.Npm, result.Source);
		Assert.Equal(NpmNative, result.ExecutablePath);
	}

	[Fact]
	public void OnlyTheNpmShim_IsNotFound()
	{
		_env.Files.Add(NpmShim);

		var result = _locator.Locate(null);

		Assert.False(result.Found);
		Assert.Equal("Codex not found. Install it with: winget install OpenAI.Codex", result.Problem);
	}

	[Fact]
	public void PathIsLast_AndOnlyCodexExeCounts()
	{
		_env.Files.UnionWith([@"C:\Tools\codex.cmd", @"C:\Windows\codex.exe"]);

		var result = _locator.Locate(null);

		Assert.Equal(CodexExecutableSource.Path, result.Source);
		Assert.Equal(@"C:\Windows\codex.exe", result.ExecutablePath);
	}

	[Fact]
	public void ALinkToAShim_IsSkipped()
	{
		_env.Files.UnionWith([WingetLink, @"C:\Tools\codex.cmd"]);
		_env.Links[WingetLink] = @"C:\Tools\codex.cmd";

		Assert.False(_locator.Locate(null).Found);
	}

	[Fact]
	public void ConfiguredPath_IsUsedAlone()
	{
		_env.Files.UnionWith([Installer, @"D:\Codex\codex.exe"]);

		var result = _locator.Locate(@"D:\Codex\codex.exe");

		Assert.Equal(CodexExecutableSource.Configured, result.Source);
		Assert.Equal(@"D:\Codex\codex.exe", result.ExecutablePath);
	}

	[Fact]
	public void ConfiguredShim_IsRejected()
	{
		_env.Files.Add(NpmShim);

		var result = _locator.Locate(NpmShim);

		Assert.False(result.Found);
		Assert.Contains("script shim", result.Problem);
	}

	[Fact]
	public void ConfiguredPathThatIsMissing_SaysSo_WithoutFallingBack()
	{
		_env.Files.Add(Installer);

		var result = _locator.Locate(@"D:\Nope\codex.exe");

		Assert.False(result.Found);
		Assert.Equal(@"The Codex path D:\Nope\codex.exe does not exist.", result.Problem);
	}

	[Theory]
	[InlineData("codex-cli 0.171.0", "0.171.0")]
	[InlineData("codex-cli 0.172.3-alpha.1", "0.172.3")]
	[InlineData("0.171.2", "0.171.2")]
	[InlineData("v1.0.0", "1.0.0")]
	public void ParseVersion_ReadsTheVersion(string output, string expected) =>
		Assert.Equal(Version.Parse(expected), _locator.ParseVersion(output));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("error: something")]
	public void ParseVersion_OfNoVersion_IsNull(string? output) => Assert.Null(_locator.ParseVersion(output));

	[Fact]
	public void TestedVersion_MatchesOnMajorAndMinor_AndAnotherGivesTheNote()
	{
		Assert.True(_locator.IsTestedVersion(new Version(0, 171, 9)));
		Assert.Null(_locator.VersionNote(new Version(0, 171, 0)));

		Assert.False(_locator.IsTestedVersion(new Version(0, 172, 0)));
		Assert.Equal("Codex 0.172.0 is newer than the tested 0.171. If GPT sessions misbehave, install 0.171.", _locator.VersionNote(new Version(0, 172, 0)));
	}

	[Fact]
	public void StatusLine_SaysWhereCodexIs_WithTheNoteForAnotherVersion_OrWhyItIsMissing()
	{
		var found = new CodexLocateResult(WingetTarget, WingetLink, CodexExecutableSource.Winget, null);

		Assert.Equal($"Codex 0.171.0 found at {WingetTarget}", _locator.StatusLine(found, new Version(0, 171, 0)));
		Assert.EndsWith("is newer than the tested 0.171. If GPT sessions misbehave, install 0.171.", _locator.StatusLine(found, new Version(0, 172, 1)));
		Assert.Equal("Codex not found. Install it with: winget install OpenAI.Codex", _locator.StatusLine(_locator.Locate(null), null));
	}

	[Fact]
	public async Task ReadVersion_RunsTheExe_AndParsesItsOutput()
	{
		_env.VersionOutputs[WingetTarget] = "codex-cli 0.171.0";

		Assert.Equal(new Version(0, 171, 0), await _locator.ReadVersionAsync(WingetTarget));
		Assert.Null(await _locator.ReadVersionAsync(@"C:\missing.exe"));
	}
}
