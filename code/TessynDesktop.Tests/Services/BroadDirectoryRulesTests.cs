using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The breadth and shape rules of tree directories (J1 review T1). Text only: none of these paths has to exist.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BroadDirectoryRulesTests
{
	private const string Profile  = @"C:\Users\me";
	private const string DataRoot = @"C:\Users\me\AppData\Roaming\TessynDesktop";

	private readonly BroadDirectoryRules _rules = new([DataRoot], [Profile]);

	[Theory]
	[InlineData(@"C:\proj", true)]
	[InlineData("C:/proj", true)]
	[InlineData(@"C:\", true)]
	[InlineData(@"\\server\share\proj", false)]
	[InlineData("//server/share/proj", false)]
	[InlineData(@"\\?\C:\proj", false)]
	[InlineData(@"\\.\C:\proj", false)]
	[InlineData("proj", false)]
	[InlineData(@".\proj", false)]
	[InlineData(@"..\proj", false)]
	[InlineData("C:proj", false)]
	[InlineData(@"\proj", false)]
	[InlineData(@"C:\proj:stream", false)]
	[InlineData("C:\\pro\0j", false)]
	[InlineData("", false)]
	public void HasAcceptableShape_AcceptsOnlyAnAbsoluteDrivePath(string path, bool expected) =>
		Assert.Equal(expected, _rules.HasAcceptableShape(path));

	[Theory]
	[InlineData(@"C:\proj\", @"C:\proj")]
	[InlineData(@"C:\proj", @"C:\proj")]
	[InlineData(@"C:\proj\\", @"C:\proj")]
	[InlineData("C:/proj/sub/", @"C:\proj\sub")]
	[InlineData(@"C:\proj\..\other", @"C:\other")]
	[InlineData(@"  C:\proj  ", @"C:\proj")]
	[InlineData(@"C:\", @"C:\")]
	public void Normalize_StripsTheTrailingSeparator_AndKeepsADriveRoot(string path, string expected) =>
		Assert.Equal(expected, _rules.Normalize(path));

	[Theory]
	[InlineData(@"\\server\share")]
	[InlineData("relative")]
	public void Normalize_ReturnsNullForAnUnacceptableShape(string path) =>
		Assert.Null(_rules.Normalize(path));

	[Theory]
	[InlineData(@"C:\", true)]
	[InlineData(@"D:\", true)]
	[InlineData(@"C:\Users", true)]                                    // a parent of the profile
	[InlineData(@"C:\Users\me", true)]                                 // the profile
	[InlineData(@"C:\Users\me\", true)]
	[InlineData(@"C:\users\ME", true)]                                 // case-insensitive
	[InlineData(@"C:\Users\me\AppData", true)]                         // a parent of the data root
	[InlineData(@"C:\Users\me\AppData\Roaming", true)]
	[InlineData(@"C:\Users\me\AppData\Roaming\TessynDesktop", true)]   // the data root
	[InlineData(@"C:\Users\me\AppData\Roaming\TessynDesktop\profiles\p1", true)] // below it
	[InlineData(@"C:\Users\me\proj", false)]
	[InlineData(@"C:\Users\me\AppData\Local\proj", false)]
	[InlineData(@"C:\Users\me2", false)]                               // a sibling that only shares the prefix
	[InlineData(@"C:\Users\me\AppData\Roaming\TessynDesktopOther", false)]
	[InlineData(@"C:\work\proj", false)]
	[InlineData(@"D:\proj", false)]
	[InlineData(@"\\server\share\proj", true)]                         // not a shape a directory may have
	[InlineData("relative", true)]
	public void IsTooBroad_JudgesTheDriveRoot_TheProfileAndItsParents_AndTheDataRootAndItsNeighbours(string path, bool expected) =>
		Assert.Equal(expected, _rules.IsTooBroad(path));

	[Fact]
	public void IsTooBroad_KnowsTheCanonicalFormOfTheDataRootAndTheProfileToo()
	{
		var rules = new BroadDirectoryRules([DataRoot, @"D:\Real\TessynDesktop"], [Profile, @"D:\Real\me"]);

		Assert.True(rules.IsTooBroad(@"D:\Real\TessynDesktop"));
		Assert.True(rules.IsTooBroad(@"D:\Real\me"));
		Assert.True(rules.IsTooBroad(@"D:\Real"));
		Assert.False(rules.IsTooBroad(@"D:\Real\me\proj"));
	}

	[Fact]
	public void TheDefaultRules_RefuseTheRealProfileAndDataRoot()
	{
		var rules = new BroadDirectoryRules();

		Assert.True(rules.IsTooBroad(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)));
		Assert.True(rules.IsTooBroad(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), Constants.AppDataFolderName)));
	}
}
