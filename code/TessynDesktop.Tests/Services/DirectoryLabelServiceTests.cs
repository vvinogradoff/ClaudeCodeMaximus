using System.IO;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class DirectoryLabelServiceTests
{
	private readonly DirectoryLabelService _sut = new();

	[Fact]
	public void GetLabel_WhenGitRootIsTheDirectory_ReturnsDirectoryNameOnly()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), "cm_test_gitroot");
		var gitDir = Path.Combine(tempDir, ".git");
		Directory.CreateDirectory(gitDir);

		try
		{
			var label = _sut.GetLabel(tempDir);
			Assert.Equal("cm_test_gitroot", label);
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	[Fact]
	public void GetLabel_WhenGitRootIsTheDirectory_WithTrailingSeparator_ReturnsDirectoryNameOnly()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), "cm_test_gitroot_trail");
		var gitDir  = Path.Combine(tempDir, ".git");
		Directory.CreateDirectory(gitDir);

		try
		{
			// Folder pickers on Windows return paths with a trailing separator
			var pathWithTrailingSep = tempDir + Path.DirectorySeparatorChar;
			var label = _sut.GetLabel(pathWithTrailingSep);
			Assert.Equal("cm_test_gitroot_trail", label);
			Assert.NotEmpty(label);
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	[Fact]
	public void GetLabel_WhenGitRootIsAncestor_ReturnsGitRootNamePlusRelativePath()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), "cm_test_ancestor");
		var subDir = Path.Combine(tempDir, "Datum.Web", "EmailTemplates");
		var gitDir = Path.Combine(tempDir, ".git");
		Directory.CreateDirectory(subDir);
		Directory.CreateDirectory(gitDir);

		try
		{
			var label = _sut.GetLabel(subDir);
			Assert.Equal(Path.Combine("cm_test_ancestor", "Datum.Web", "EmailTemplates"), label);
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	[Fact]
	public void GetLabel_WhenNoGitRoot_ReturnsFullPath()
	{
		// Use a temp path that definitely has no .git above it
		var tempDir = Path.Combine(Path.GetTempPath(), "cm_test_nogit_" + Path.GetRandomFileName());
		Directory.CreateDirectory(tempDir);

		try
		{
			var label = _sut.GetLabel(tempDir);
			Assert.Equal(Path.GetFullPath(tempDir), label);
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}
}
