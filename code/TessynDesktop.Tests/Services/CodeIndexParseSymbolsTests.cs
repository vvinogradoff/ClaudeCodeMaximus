using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.CR.19: an index created while Code references is off keeps its files but parses no C# symbols; an index created while it was on keeps them (FR.7.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodeIndexParseSymbolsTests : IDisposable
{
	private const string FooSource = "namespace N { public class Foo { public void Bar() {} } }";

	private readonly string _root = Path.Combine(Path.GetTempPath(), "cm_test_parsesymbols_" + Path.GetRandomFileName());

	public CodeIndexParseSymbolsTests()
	{
		Directory.CreateDirectory(Path.Combine(_root, "src"));
		File.WriteAllText(Path.Combine(_root, "src", "Foo.cs"), FooSource);
		File.WriteAllText(Path.Combine(_root, "readme.md"), "x");
	}

	public void Dispose() => Directory.Delete(_root, recursive: true);

	[Fact]
	public async Task TCCR19_WithParseSymbolsOff_AllFilesAreIndexed_AndNoSymbolIsHeld()
	{
		using var index = new CodeIndex(_root, enableWatcher: false, parseSymbols: false);

		await index.BuildAsync();

		Assert.Equal(["readme.md", "src/Foo.cs"], index.FileSnapshot.Select(f => f.RelativePath).Order(StringComparer.Ordinal));
		Assert.Empty(index.SymbolSnapshot);
	}

	[Fact]
	public async Task TCCR19_WithParseSymbolsOn_TheClassIsHeld()
	{
		using var index = new CodeIndex(_root, enableWatcher: false, parseSymbols: true);

		await index.BuildAsync();

		Assert.Contains(index.SymbolSnapshot, s => s.Name == "Foo");
	}

	[Fact]
	public async Task TCCR19_TheServiceFollowsTheSettingAtCreationTime_AndNeverTouchesAnExistingIndex()
	{
		var settingsDir = Path.Combine(_root, "settings");
		Directory.CreateDirectory(settingsDir);
		var settings = new AppSettingsService(settingsDir);
		var service = new CodeIndexService(settings);

		// Created while OFF (the default): files, no symbols.
		Assert.False(settings.Settings.CodeReferencesEnabled);
		Assert.True(await service.GetOrCreateIndexAsync(_root));
		Assert.NotEmpty(service.SearchFiles(_root, "Foo"));
		Assert.Empty(service.SearchSymbols(_root, "Foo"));

		// Turned ON: the existing index is not rebuilt (it keeps what it has) ...
		settings.Settings.CodeReferencesEnabled = true;
		Assert.True(await service.GetOrCreateIndexAsync(_root));
		Assert.Empty(service.SearchSymbols(_root, "Foo"));
		service.ReleaseIndex(_root);
		service.ReleaseIndex(_root);

		// ... and an index created after the flip parses symbols.
		Assert.True(await service.GetOrCreateIndexAsync(_root));
		Assert.NotEmpty(service.SearchSymbols(_root, "Foo"));

		// Turned OFF again: the index that holds symbols keeps them (not dropped).
		settings.Settings.CodeReferencesEnabled = false;
		Assert.NotEmpty(service.SearchSymbols(_root, "Foo"));
		service.ReleaseIndex(_root);
	}

	[Fact]
	public async Task TCCR19_WithoutASettingsSource_SymbolsAreParsed()
	{
		var service = new CodeIndexService();

		Assert.True(await service.GetOrCreateIndexAsync(_root));
		Assert.NotEmpty(service.SearchSymbols(_root, "Foo"));
		service.ReleaseIndex(_root);
	}

	[Fact]
	public async Task TCCR19_WithParseSymbolsOff_TheWatcherStillIndexesNewFiles_WithoutSymbols()
	{
		using var index = new CodeIndex(_root, enableWatcher: true, parseSymbols: false);
		await index.BuildAsync();

		File.WriteAllText(Path.Combine(_root, "src", "Added.cs"), "namespace N { public class Added {} }");

		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (index.FileSnapshot.All(f => f.FileName != "Added.cs") && DateTime.UtcNow < deadline)
			await Task.Delay(100);

		Assert.Contains(index.FileSnapshot, f => f.FileName == "Added.cs");
		Assert.Empty(index.SymbolSnapshot);
	}
}
