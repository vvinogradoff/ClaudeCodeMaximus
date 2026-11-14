using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude. Covers the incremental CodeIndex (ISSUE.016): changes touch only their own entries, excluded trees stay out.</remarks>
public sealed class CodeIndexTests : IDisposable
{
	private readonly string _root;

	public CodeIndexTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "cm_test_codeindex_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_root);
	}

	public void Dispose() => Directory.Delete(_root, recursive: true);

	private string Write(string relative, string content = "")
	{
		var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllText(full, content);
		return full;
	}

	private async Task<CodeIndex> BuildAsync()
	{
		var index = new CodeIndex(_root, enableWatcher: false);
		await index.BuildAsync();
		return index;
	}

	private static string[] RelativePaths(CodeIndex index) =>
		index.FileSnapshot.Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToArray();

	[Fact]
	public async Task Build_IndexesFilesAndSymbols_AndSkipsExcludedDirectories()
	{
		Write("src/Foo.cs", "namespace N { public class Foo { public void Bar() {} } }");
		Write("readme.md");
		foreach (var excluded in new[] { "bin", "obj", ".git", "node_modules", "vcpkg", ".venv" })
			Write($"{excluded}/skipped.txt");

		using var index = await BuildAsync();

		Assert.Equal(new[] { "readme.md", "src/Foo.cs" }, RelativePaths(index));
		Assert.Contains(index.SymbolSnapshot, s => s.Name == "Foo");
		Assert.Contains(index.SymbolSnapshot, s => s.Name == "Bar");
	}

	[Fact]
	public async Task IndexedFile_ComputesAbsolutePathFromSharedRoot()
	{
		var full = Write("a/b.txt");
		using var index = await BuildAsync();

		var file = Assert.Single(index.FileSnapshot);
		Assert.Equal("b.txt", file.FileName);
		Assert.Equal("a/b.txt", file.RelativePath);
		Assert.Equal(Path.GetFullPath(full), Path.GetFullPath(file.AbsolutePath));
	}

	[Fact]
	public async Task ApplyChanges_AddedFile_AppearsWithoutRescan()
	{
		Write("one.txt");
		using var index = await BuildAsync();

		var added = Write("sub/two.cs", "class Two {}");
		index.ApplyChanges(new[] { added });

		Assert.Equal(new[] { "one.txt", "sub/two.cs" }, RelativePaths(index));
		Assert.Contains(index.SymbolSnapshot, s => s.Name == "Two");
	}

	[Fact]
	public async Task ApplyChanges_ChangedCsFile_ReplacesItsSymbolsOnly()
	{
		var a = Write("A.cs", "class Alpha {}");
		Write("B.cs", "class Beta {}");
		using var index = await BuildAsync();

		File.WriteAllText(a, "class Gamma {}");
		index.ApplyChanges(new[] { a });

		var names = index.SymbolSnapshot.Select(s => s.Name).OrderBy(n => n).ToArray();
		Assert.Equal(new[] { "Beta", "Gamma" }, names);
	}

	[Fact]
	public async Task ApplyChanges_DeletedFile_IsRemovedWithItsSymbols()
	{
		var a = Write("A.cs", "class Alpha {}");
		Write("B.cs", "class Beta {}");
		using var index = await BuildAsync();

		File.Delete(a);
		index.ApplyChanges(new[] { a });

		Assert.Equal(new[] { "B.cs" }, RelativePaths(index));
		Assert.DoesNotContain(index.SymbolSnapshot, s => s.Name == "Alpha");
	}

	[Fact]
	public async Task ApplyChanges_DeletedDirectory_RemovesEverythingBelowIt()
	{
		Write("keep.txt");
		Write("dir/x.cs", "class X {}");
		Write("dir/deep/y.txt");
		Write("dirother/z.txt");
		using var index = await BuildAsync();

		var dir = Path.Combine(_root, "dir");
		Directory.Delete(dir, recursive: true);
		index.ApplyChanges(new[] { dir });

		Assert.Equal(new[] { "dirother/z.txt", "keep.txt" }, RelativePaths(index));
		Assert.Empty(index.SymbolSnapshot);
	}

	[Fact]
	public async Task ApplyChanges_NewDirectory_ScansItsSubtree()
	{
		Write("keep.txt");
		using var index = await BuildAsync();

		Write("fresh/a.txt");
		Write("fresh/inner/b.cs", "class B {}");
		index.ApplyChanges(new[] { Path.Combine(_root, "fresh") });

		Assert.Equal(new[] { "fresh/a.txt", "fresh/inner/b.cs", "keep.txt" }, RelativePaths(index));
		Assert.Contains(index.SymbolSnapshot, s => s.Name == "B");
	}

	[Fact]
	public async Task ApplyChanges_PathInsideExcludedDirectory_IsIgnored()
	{
		Write("keep.txt");
		using var index = await BuildAsync();

		var skipped = Write("node_modules/pkg/index.js");
		index.ApplyChanges(new[] { skipped });

		Assert.Equal(new[] { "keep.txt" }, RelativePaths(index));
	}

	[Fact]
	public async Task ApplyChanges_RenamedFile_RemovesOldAndAddsNew()
	{
		var old = Write("old.txt");
		using var index = await BuildAsync();

		var renamed = Path.Combine(_root, "new.txt");
		File.Move(old, renamed);
		index.ApplyChanges(new[] { old, renamed });

		Assert.Equal(new[] { "new.txt" }, RelativePaths(index));
	}

	[Fact]
	public async Task Snapshot_IsStableBetweenChanges_AndRebuiltAfterOne()
	{
		Write("a.txt");
		using var index = await BuildAsync();

		var first = index.FileSnapshot;
		Assert.Same(first, index.FileSnapshot);

		index.ApplyChanges(new[] { Write("b.txt") });
		Assert.NotSame(first, index.FileSnapshot);
		Assert.Equal(2, index.FileSnapshot.Length);
	}

	[Fact]
	public async Task Dispose_ClearsIndexData()
	{
		Write("a.cs", "class A {}");
		var index = await BuildAsync();

		index.Dispose();

		Assert.Empty(index.FileSnapshot);
		Assert.Empty(index.SymbolSnapshot);
	}

	[Fact]
	public async Task Watcher_PicksUpANewFile_Eventually()
	{
		Write("a.txt");
		using var index = new CodeIndex(_root);
		await index.BuildAsync();

		Write("late.txt");

		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (DateTime.UtcNow < deadline && !index.FileSnapshot.Any(f => f.RelativePath == "late.txt"))
			await Task.Delay(100);

		Assert.Contains(index.FileSnapshot, f => f.RelativePath == "late.txt");
	}

	[Fact]
	public async Task Service_ReleasingLastReference_DisposesIndex_AndSearchFindsNothing()
	{
		Write("needle.txt");
		var service = new CodeIndexService();

		await service.GetOrCreateIndexAsync(_root);
		await service.GetOrCreateIndexAsync(_root);
		Assert.Single(service.SearchFiles(_root, "needle"));

		service.ReleaseIndex(_root);
		Assert.True(service.IsIndexReady(_root));

		service.ReleaseIndex(_root);
		Assert.False(service.IsIndexReady(_root));
		Assert.Empty(service.SearchFiles(_root, "needle"));
	}

	[Theory]
	[InlineData("obj")]
	[InlineData("bin")]
	[InlineData("node_modules")]
	[InlineData(".git")]
	public async Task ApplyChanges_CreatedExcludedDirectory_IsNotIndexed(string name)
	{
		Write("keep.txt");
		using var index = await BuildAsync();

		var generated = Write($"{name}/generated.dll");
		Write($"{name}/sub/more.cs", "class More {}");
		index.ApplyChanges(new[] { Path.GetDirectoryName(generated)! });

		Assert.Equal(new[] { "keep.txt" }, RelativePaths(index));
		Assert.DoesNotContain(index.SymbolSnapshot, s => s.Name == "More");
	}

	[Fact]
	public async Task ApplyChanges_FileNamedLikeExcludedDirectory_IsStillIndexed()
	{
		using var index = await BuildAsync();

		var file = Write("bin");
		index.ApplyChanges(new[] { file });

		Assert.Equal(new[] { "bin" }, RelativePaths(index));
	}

	[Fact]
	public async Task ApplyChanges_ReplacedSubtree_IsSwappedInOneStep()
	{
		Write("d/old.txt");
		using var index = await BuildAsync();

		File.Delete(Path.Combine(_root, "d", "old.txt"));
		Write("d/new.txt");
		index.ApplyChanges(new[] { Path.Combine(_root, "d") });

		Assert.Equal(new[] { "d/new.txt" }, RelativePaths(index));
	}

	[Fact]
	public async Task Build_ReleasedDuringFirstScan_StartsNoWatcherAndIsNotReady()
	{
		Write("a.txt");
		var index = new CodeIndex(_root, enableWatcher: true);
		index.ScanCompletedHook = index.Dispose;

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => index.BuildAsync());

		Assert.False(index.HasWatcher);
		Assert.False(index.IsReady);
	}

	[Fact]
	public async Task Service_FailedBuild_ReturnsFalse_AndDoesNotCountAReference()
	{
		Write("needle.txt");
		var service = new CodeIndexService
		{
			IndexFactory = dir => new CodeIndex(dir, enableWatcher: false) { ScanCompletedHook = () => throw new InvalidOperationException("boom") },
		};

		Assert.False(await service.GetOrCreateIndexAsync(_root));
		Assert.False(service.IsIndexReady(_root));

		// A healthy index for the same directory is created afterwards and holds exactly one reference.
		service.IndexFactory = dir => new CodeIndex(dir, enableWatcher: false);
		Assert.True(await service.GetOrCreateIndexAsync(_root));
		Assert.True(service.IsIndexReady(_root));

		// The failed caller got false, so it does not release; the healthy index must still be there.
		Assert.Single(service.SearchFiles(_root, "needle"));
		service.ReleaseIndex(_root);
		Assert.False(service.IsIndexReady(_root));
	}
}
