using TessynDesktop.WPF.Switch;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>Finding the newest build, recognising a build output, and copying a build into a run folder (W13, decision A23 Q7).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SwitchBuildTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-switch-build-" + Guid.NewGuid().ToString("N"));

	public SwitchBuildTests()
	{
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		TestDirectory.Delete(_root);
	}

	private string MakeBuild(string project, string configuration, string tfm, string assembly, DateTime stamp, string? underCode = "code")
	{
		var dir = Path.Combine(_root, underCode ?? "", project, "bin", configuration, tfm);
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, assembly);
		File.WriteAllText(path, assembly);
		File.SetLastWriteTimeUtc(path, stamp);
		return dir;
	}

	[Fact]
	public void FindNewestBuild_PicksTheNewestAssemblyAcrossConfigurationsAndFrameworks()
	{
		var old = DateTime.UtcNow.AddHours(-3);
		MakeBuild("TessynDesktop.WPF", "Debug", "net10.0-windows10.0.17763.0", "TessynDesktop.WPF.dll", old);
		var newest = MakeBuild("TessynDesktop.WPF", "Release", "net10.0-windows10.0.17763.0", "TessynDesktop.WPF.dll", old.AddHours(2));

		Assert.Equal(newest, SourceBuilds.FindNewestBuild(_root, "TessynDesktop.WPF", "TessynDesktop.WPF.dll"));
	}

	[Fact]
	public void FindNewestBuild_IgnoresAnotherProjectAndFoldersWithoutTheAssembly()
	{
		MakeBuild("TessynDesktop", "Debug", "net10.0-windows10.0.17763.0", "TessynDesktop.dll", DateTime.UtcNow);
		Directory.CreateDirectory(Path.Combine(_root, "code", "TessynDesktop.WPF", "bin", "Debug", "net10.0-windows10.0.17763.0"));

		Assert.Null(SourceBuilds.FindNewestBuild(_root, "TessynDesktop.WPF", "TessynDesktop.WPF.dll"));
	}

	[Fact]
	public void FindNewestBuild_AcceptsASourceLocationThatIsTheCodeFolderItself()
	{
		var dir = MakeBuild("TessynDesktop.WPF", "Debug", "net10.0", "TessynDesktop.WPF.dll", DateTime.UtcNow, underCode: null);

		Assert.Equal(dir, SourceBuilds.FindNewestBuild(_root, "TessynDesktop.WPF", "TessynDesktop.WPF.dll"));
	}

	[Theory]
	[InlineData(@"C:\Projects\Tessyn\code\TessynDesktop.WPF\bin\Debug\net10.0-windows10.0.17763.0", true)]
	[InlineData(@"C:\Projects\Tessyn\code\TessynDesktop.WPF\bin\Release\net10.0-windows10.0.17763.0\", true)]
	[InlineData(@"C:\Projects\Tessyn\[Publish-WPF]", false)]
	[InlineData(@"C:\Projects\Tessyn\[Publish-WPF]\host", false)]
	[InlineData(@"C:\bin\tools", false)]
	public void IsBuildOutput_RecognisesBinDebugAndBinRelease(string folder, bool expected)
	{
		Assert.Equal(expected, SourceBuilds.IsBuildOutput(folder));
	}

	[Fact]
	public void ReadSourceLocation_ReadsTheSettingsFileCaseInsensitively_AndNullWhenMissingOrBroken()
	{
		var file = Path.Combine(_root, "appsettings.json");
		Assert.Null(new SourceBuilds(file).ReadSourceLocation());

		File.WriteAllText(file, "{ not json");
		Assert.Null(new SourceBuilds(file).ReadSourceLocation());

		File.WriteAllText(file, "{ \"sourceCodesLocation\": \"C:\\\\Projects\\\\Personal\\\\Tessyn\", \"Other\": 1 }");
		Assert.Equal(@"C:\Projects\Personal\Tessyn", new SourceBuilds(file).ReadSourceLocation());

		File.WriteAllText(file, "{ \"SourceCodesLocation\": \"\" }");
		Assert.Null(new SourceBuilds(file).ReadSourceLocation());
	}

	[Fact]
	public void Copy_CopiesTheTreeRecursively_AndASecondRunCopiesNothing()
	{
		var source = Path.Combine(_root, "src");
		Directory.CreateDirectory(Path.Combine(source, "runtimes", "win"));
		File.WriteAllText(Path.Combine(source, "a.dll"), "a");
		File.WriteAllText(Path.Combine(source, "runtimes", "win", "native.dll"), "native");
		var destination = Path.Combine(_root, "dst");

		var first = BuildCopier.Copy(source, destination);
		var second = BuildCopier.Copy(source, destination);

		Assert.Equal(2, first.Copied);
		Assert.Equal("native", File.ReadAllText(Path.Combine(destination, "runtimes", "win", "native.dll")));
		Assert.Equal(0, second.Copied);
		Assert.Equal(2, second.UpToDate);
		Assert.True(second.IsComplete);
	}

	[Fact]
	public void Copy_ReplacesAChangedFile_AndNeverDeletesWhatIsOnlyInTheDestination()
	{
		var source = Path.Combine(_root, "src");
		var destination = Path.Combine(_root, "dst");
		Directory.CreateDirectory(source);
		Directory.CreateDirectory(Path.Combine(destination, "host"));
		File.WriteAllText(Path.Combine(destination, "host", "TessynDesktop.exe"), "host");
		File.WriteAllText(Path.Combine(destination, "a.dll"), "old");
		File.WriteAllText(Path.Combine(source, "a.dll"), "new and longer");

		var result = BuildCopier.Copy(source, destination);

		Assert.Equal(1, result.Copied);
		Assert.Equal("new and longer", File.ReadAllText(Path.Combine(destination, "a.dll")));
		Assert.Equal("host", File.ReadAllText(Path.Combine(destination, "host", "TessynDesktop.exe")));
	}

	[Fact]
	public void Copy_SkipsALockedFileAndStillCopiesTheOthers()
	{
		var source = Path.Combine(_root, "src");
		var destination = Path.Combine(_root, "dst");
		Directory.CreateDirectory(source);
		Directory.CreateDirectory(destination);
		File.WriteAllText(Path.Combine(source, "locked.dll"), "new");
		File.WriteAllText(Path.Combine(source, "free.dll"), "free");
		File.WriteAllText(Path.Combine(destination, "locked.dll"), "old!");

		using (new FileStream(Path.Combine(destination, "locked.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
		{
			var result = BuildCopier.Copy(source, destination);

			Assert.Equal(1, result.Copied);
			Assert.Equal(1, result.Skipped);
			Assert.False(result.IsComplete);
		}

		Assert.Equal("free", File.ReadAllText(Path.Combine(destination, "free.dll")));
		Assert.Equal("old!", File.ReadAllText(Path.Combine(destination, "locked.dll")));
	}
}
