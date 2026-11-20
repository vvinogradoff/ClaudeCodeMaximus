using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The security rule of the preview pane: only documents and media may be handed to the default app; an executable or script never.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ExternalOpenPolicyTests
{
	private readonly ExternalOpenPolicy _policy = new();

	[Theory]
	[InlineData("plan.md")]
	[InlineData("README.MARKDOWN")]
	[InlineData("notes.txt")]
	[InlineData("photo.PNG")]
	[InlineData("a.jpg")]
	[InlineData("a.jpeg")]
	[InlineData("a.gif")]
	[InlineData("a.webp")]
	[InlineData("clip.mp4")]
	[InlineData("clip.webm")]
	[InlineData("clip.mov")]
	[InlineData("doc.pdf")]
	[InlineData(@"C:\work\docs\report.pdf")]
	[InlineData("page.html")]
	[InlineData("page.HTM")]
	[InlineData("dir/with/slashes/readme.md")]
	public void DocumentsAndMedia_AreAllowed(string name) => Assert.True(_policy.IsAllowed(name));

	[Theory]
	[InlineData("setup.exe")]
	[InlineData("run.bat")]
	[InlineData("run.cmd")]
	[InlineData("script.ps1")]
	[InlineData("module.psm1")]
	[InlineData("shortcut.lnk")]
	[InlineData("link.url")]
	[InlineData("code.js")]
	[InlineData("code.mjs")]
	[InlineData("code.vbs")]
	[InlineData("code.vbe")]
	[InlineData("code.wsf")]
	[InlineData("code.hta")]
	[InlineData("page.html.js")]
	[InlineData("installer.msi")]
	[InlineData("saver.scr")]
	[InlineData("old.com")]
	[InlineData("old.pif")]
	[InlineData("lib.dll")]
	[InlineData("tool.jar")]
	[InlineData("settings.reg")]
	[InlineData("applet.cpl")]
	[InlineData("drawing.svg")]
	[InlineData("script.sh")]
	[InlineData("Program.cs")]
	[InlineData("noextension")]
	[InlineData(".md")]
	[InlineData("")]
	[InlineData(null)]
	public void ExecutablesScriptsAndOtherTypes_AreRefused(string? name) => Assert.False(_policy.IsAllowed(name));

	[Theory]
	[InlineData("run.exe.")]
	[InlineData("run.exe ")]
	[InlineData("run.exe. . ")]
	[InlineData("report.pdf:evil.exe")]
	[InlineData("report.pdf:$DATA")]
	[InlineData(@"C:\x\run.EXE")]
	[InlineData("a.md\u0000.exe")]
	[InlineData("a.pdf\n.exe")]
	public void TricksThatResolveToAnExecutable_AreRefused(string name) => Assert.False(_policy.IsAllowed(name));

	[Theory]
	[InlineData("annual\u202Efdp.png")]                 // right-to-left override: would display as annualgnp.pdf
	[InlineData("report\u200B.pdf")]                    // zero-width space
	[InlineData("report\u200D.pdf")]
	[InlineData("report\uFEFF.md")]                      // byte-order mark
	[InlineData("a\uE000b.png")]                         // private use
	[InlineData("a\uD83D\uDE00b.png")]                   // surrogate pair (emoji)
	[InlineData("notes.\u212Aarkdown")]                  // Kelvin sign: culture lowering would read it as ".markdown"
	[InlineData("notes.mar\u212Adown")]
	[InlineData("clip.mp\u0034\u00AD")]                  // soft hyphen (format)
	[InlineData("doc.p\u0064f\u202E")]
	[InlineData("picture.pn\u0261")]                     // non-ASCII extension look-alike
	public void SpoofingCharactersAndNonAsciiExtensions_AreRefused(string name)
	{
		Assert.False(_policy.IsAllowed(name));
		Assert.False(new ExternalOpenPolicy().IsAllowed(@"C:\x\" + name));
	}

	[Fact]
	public void TheDisplayName_ReplacesSpoofingCharacters_SoTheVisibleExtensionIsTheRealOne()
	{
		var shown = _policy.DisplayName("annual\u202Efdp.png");

		Assert.DoesNotContain('\u202E', shown);
		Assert.EndsWith("fdp.png", shown);
		Assert.Equal("plain.pdf", _policy.DisplayName("plain.pdf"));
	}

	[Theory]
	[InlineData("NUL.png")]
	[InlineData("COM1.pdf")]
	[InlineData("report~1.pdf")]       // 8.3 alias form: judged by its own extension only
	[InlineData("PROGRA~1.md")]
	public void ReservedAndShortNames_AreJudgedByTheirExtensionOnly_TheCacheNeverUsesThem(string name) =>
		Assert.True(_policy.IsAllowed(name));   // allowed to be opened; the cache file is always called "content" + the extension (see FilePreviewLoaderTests)

	[Theory]
	[InlineData("RUN~1.EXE")]
	[InlineData("SCRIPT~1.PS1")]
	[InlineData("INSTAL~1.MSI")]
	public void ShortAliasesOfExecutables_AreRefused(string name) => Assert.False(_policy.IsAllowed(name));

	[Fact]
	public void TheLauncherRefusesAFileReachedThroughAJunctionInsideTheCache()
	{
		using var temp = new TempAppPaths();
		var launcher = new SystemExternalFileLauncher(new ExternalOpenPolicy(), temp.Paths, NullLogger<SystemExternalFileLauncher>.Instance);
		var outside = Path.Combine(temp.Root, "outside");
		Directory.CreateDirectory(outside);
		File.WriteAllText(Path.Combine(outside, "content.pdf"), "%PDF-");
		Directory.CreateDirectory(temp.Paths.PreviewCacheDirectory);
		var junction = Path.Combine(temp.Paths.PreviewCacheDirectory, "abc");

		var made = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true } };
		made.Start();
		made.StandardOutput.ReadToEnd();
		made.WaitForExit();
		Assert.True(Directory.Exists(junction), "the test could not create a junction");

		Assert.False(launcher.TryOpen(Path.Combine(junction, "content.pdf")));
	}

	[Fact]
	public void TheLeadingDirectoryNamesDoNotMatter_OnlyTheFileName()
	{
		Assert.False(_policy.IsAllowed(@"C:\docs.pdf\run.exe"));
		Assert.True(_policy.IsAllowed(@"C:\tools.exe\readme.md"));
	}

	[Fact]
	public void TheLauncherRefusesADisallowedTypeInsideTheCache_AndAnAllowedTypeOutsideIt()
	{
		using var temp = new TempAppPaths();
		var launcher = new SystemExternalFileLauncher(new ExternalOpenPolicy(), temp.Paths, NullLogger<SystemExternalFileLauncher>.Instance);
		Directory.CreateDirectory(temp.Paths.PreviewCacheDirectory);
		var inCache = Path.Combine(temp.Paths.PreviewCacheDirectory, "evil.bat");
		File.WriteAllText(inCache, "@echo off");
		var outside = Path.Combine(temp.Root, "outside.pdf");
		File.WriteAllText(outside, "%PDF-");

		Assert.False(launcher.TryOpen(inCache));
		Assert.False(launcher.TryOpen(outside));
		Assert.False(launcher.TryOpen(Path.Combine(temp.Paths.PreviewCacheDirectory, "..", "outside.pdf")));
		Assert.False(launcher.TryOpen(Path.Combine(temp.Paths.PreviewCacheDirectory, "missing.pdf")));
	}
}
