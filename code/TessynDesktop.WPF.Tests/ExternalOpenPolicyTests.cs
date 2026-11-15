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
	[InlineData("installer.msi")]
	[InlineData("saver.scr")]
	[InlineData("old.com")]
	[InlineData("old.pif")]
	[InlineData("lib.dll")]
	[InlineData("tool.jar")]
	[InlineData("settings.reg")]
	[InlineData("applet.cpl")]
	[InlineData("page.html")]
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
