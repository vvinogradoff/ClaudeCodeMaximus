using System.IO;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class DirectoryLabelServiceTests
{
	private readonly DirectoryLabelService _sut = new();

	[Theory]
	[InlineData(@"C:\Projects\oDesk\HardStop", "HardStop")]
	[InlineData(@"C:\Projects\oDesk\HardStop\", "HardStop")]
	[InlineData(@"C:\Projects\oDesk\HardStop\Code\", "HardStop")]
	[InlineData(@"C:\Projects\oDesk\HardStop\data", "HardStop")]
	[InlineData(@"C:\Projects\oDesk\HardStop\Code\Docs", "HardStop")]
	[InlineData(@"C:\Projects\oDesk\HardStop\Code\Data", "HardStop")]
	[InlineData(@"C:\Projects\EyeLabX\Code\EyeCura\", "EyeCura")]
	[InlineData(@"C:\Projects\Personal\QuickBooksSQL\ContentManagementSystem\", "ContentManagementSystem")]
	[InlineData(@"C:\Code", "Code")]
	[InlineData(@"C:\code\", "code")]
	[InlineData(@"C:\", @"C:\")]
	[InlineData(@"C:", @"C:\")]
	[InlineData(@"\\server\share\Proj", "Proj")]
	[InlineData("/home/u/proj/Code", "proj")]
	[InlineData("/home/u/proj", "proj")]
	[InlineData("", "")]
	public void GetLabel_PathVariants_ReturnsExpectedLabel(string path, string expected)
		=> Assert.Equal(expected, _sut.GetLabel(path));

	[Fact]
	public void GetLabel_WhenWellKnownFolderCasingDiffers_MatchesCaseInsensitivelyAndKeepsParentCasing()
		=> Assert.Equal("MyApp", _sut.GetLabel(@"C:\Proj\MyApp\CODE"));

	[Fact]
	public void GetLabel_WhenLastSegmentHasMixedCasing_KeepsOriginalCasing()
		=> Assert.Equal("mYapp", _sut.GetLabel(@"C:\Proj\mYapp"));

	[Fact]
	public void GetLabel_WhenGitRootIsTheDirectory_ReturnsDirectoryName()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), "cm_test_gitroot");
		Directory.CreateDirectory(Path.Combine(tempDir, ".git"));

		try
		{
			Assert.Equal("cm_test_gitroot", _sut.GetLabel(tempDir));
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	[Fact]
	public void GetLabel_WhenGitRootIsAncestor_ReturnsOwnLastSegmentOnly()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), "cm_test_ancestor");
		var subDir = Path.Combine(tempDir, "Datum.Web", "EmailTemplates");
		Directory.CreateDirectory(subDir);
		Directory.CreateDirectory(Path.Combine(tempDir, ".git"));

		try
		{
			Assert.Equal("EmailTemplates", _sut.GetLabel(subDir));
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	[Fact]
	public void GetLabel_WhenNoGitRootAnywhere_ReturnsLastSegmentNotFullPath()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), "cm_test_nogit_" + Path.GetRandomFileName());
		Directory.CreateDirectory(tempDir);

		try
		{
			var label = _sut.GetLabel(tempDir);
			Assert.Equal(Path.GetFileName(tempDir), label);
			Assert.NotEqual(Path.GetFullPath(tempDir), label);
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	[Fact]
	public void GetLabel_WhenPathDoesNotExist_StillReturnsLastSegment()
		=> Assert.Equal("Missing", _sut.GetLabel(Path.Combine(Path.GetTempPath(), "cm_nonexistent_" + Path.GetRandomFileName(), "Missing")));
}
