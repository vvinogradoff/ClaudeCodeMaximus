using System;
using System.IO;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class WpfClientLocatorTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "wpfloc-" + Guid.NewGuid().ToString("N"));

	public WpfClientLocatorTests() => Directory.CreateDirectory(Path.Combine(_root, "[Publish]"));

	public void Dispose()
	{
		try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
	}

	[Fact]
	public void TheWpfExe_IsFoundInTheSiblingRunFolder_WithOrWithoutATrailingSlash()
	{
		var wpf = Path.Combine(_root, WpfClientLocator.RunFolderName);
		Directory.CreateDirectory(wpf);
		var exe = Path.Combine(wpf, WpfClientLocator.ExeName);
		File.WriteAllText(exe, "");

		Assert.Equal(exe, WpfClientLocator.Find(Path.Combine(_root, "[Publish]") + Path.DirectorySeparatorChar));
		Assert.Equal(exe, WpfClientLocator.Find(Path.Combine(_root, "[Publish]")));
	}

	[Fact]
	public void WithoutTheRunFolder_NothingIsFound()
	{
		Assert.Null(WpfClientLocator.Find(Path.Combine(_root, "[Publish]")));
		Assert.Null(WpfClientLocator.Find(""));
	}
}
