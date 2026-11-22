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

	/// <summary>FR.33.17 (tdcp-spec 10.A14): ChatGPT sign-in, sign-out and the settings stay on the desktop; the web only reads the status.</summary>
	public static TheoryData<string> CodexAdminMethods() =>
	[
		"profiles.codexLogin", "profiles.codexLoginCancel", "profiles.codexLogout", "profile.list", "profile.remove", "settings.get", "settings.update",
	];

	[Theory]
	[MemberData(nameof(CodexAdminMethods))]
	public void The_ChatGPT_and_settings_administration_is_not_on_the_browser_allowlist(string method)
	{
		var allowlist = new RemoteMethodAllowlist();

		Assert.False(allowlist.IsAllowed(method));
	}

	[Theory]
	[InlineData("client.hello")]
	[InlineData("catalog.get")]
	[InlineData("usage.get")]
	public void The_read_only_Codex_status_catalog_and_usage_are_on_the_browser_allowlist(string method) =>
		Assert.True(new RemoteMethodAllowlist().IsAllowed(method));

	[Theory]
	[MemberData(nameof(J1Methods))]
	public void ANewLoopbackOnlyMethod_IsNotOnTheBrowserAllowlist(string method)
	{
		var allowlist = new RemoteMethodAllowlist();

		Assert.False(allowlist.IsAllowed(method));
		Assert.DoesNotContain(method, allowlist.Methods);
	}
}
