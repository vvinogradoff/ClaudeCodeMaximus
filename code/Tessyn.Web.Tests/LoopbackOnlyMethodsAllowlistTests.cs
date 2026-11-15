using Tessyn.Web.Link;

namespace Tessyn.Web.Tests;

/// <summary>
/// The second gate of the relay: the web server's own allowlist (the daemon's dispatcher is the first). None of the loopback-only methods of
/// the J1 package (tree mutation, session options, local tools) may be callable by a browser.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LoopbackOnlyMethodsAllowlistTests
{
	public static TheoryData<string> J1Methods() =>
	[
		"tree.addDirectory", "tree.addGroup", "tree.renameNode", "tree.deleteNode", "tree.moveNode", "tree.moveTargets",
		"tree.nodeInfo", "tree.refreshGitOrigins", "session.setOptions", "session.resetConversation", "localTools.get", "localTools.set",
	];

	[Theory]
	[MemberData(nameof(J1Methods))]
	public void ANewLoopbackOnlyMethod_IsNotOnTheBrowserAllowlist(string method)
	{
		var allowlist = new RemoteMethodAllowlist();

		Assert.False(allowlist.IsAllowed(method));
		Assert.DoesNotContain(method, allowlist.Methods);
	}
}
