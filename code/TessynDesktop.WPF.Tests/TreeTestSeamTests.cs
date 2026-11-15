using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>The <c>--test-folder</c> seam (W2): a test window answers its folder dialog from the command line and never opens one.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeTestSeamTests
{
	[Fact]
	public void TestFolder_IsReadOnlyTogetherWithTestWindow()
	{
		var with = StartupOptions.Parse(["--test-window", "--test-folder", @"C:\a", "--test-folder", @"C:\b", "--data-root", "x"]);
		var without = StartupOptions.Parse(["--test-folder", @"C:\a", "--data-root", "x"]);

		Assert.Equal([@"C:\a", @"C:\b"], with.TestFolderAnswers);
		Assert.Empty(without.TestFolderAnswers);
	}

	[Fact]
	public async Task ScriptedFolderPicker_AnswersInOrder_ThenCancels()
	{
		var picker = new ScriptedFolderPicker([@"C:\a", @"C:\b"], NullLogger<ScriptedFolderPicker>.Instance);

		Assert.Equal(@"C:\a", await picker.PickAsync("Add"));
		Assert.Equal(@"C:\b", await picker.PickAsync("Add"));
		Assert.Null(await picker.PickAsync("Add"));
	}
}
