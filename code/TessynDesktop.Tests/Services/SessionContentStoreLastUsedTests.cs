using System.Linq;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>P1.G: <c>GetLastUsed</c> finds the resolver's inputs through the index and agrees with a full parse.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionContentStoreLastUsedTests : ContentStoreTestBase
{
	private const string File1 = "last-used.txt";

	/// <summary>The rule of <c>LastUsedSettingsResolver.Resolve</c> over a full parse.</summary>
	private SessionContentLastUsedModel Reference(string fileName)
	{
		var entries = Files.ReadEntries(fileName);
		var profile = entries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleUser && e.ProfileName != null);
		var model   = entries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleAssistant && e.ModelId != null);
		return new SessionContentLastUsedModel(profile?.ProfileName, model?.ModelId, model?.Effort);
	}

	private void AssertAgrees(ISessionContentStore store, string fileName) =>
		Assert.Equal(Reference(fileName), store.GetLastUsed(fileName));

	[Fact]
	public void A_missing_or_empty_file_has_nothing()
	{
		using var store = NewStore();
		Assert.Equal(new SessionContentLastUsedModel(null, null, null), store.GetLastUsed("missing.txt"));

		WriteRaw(File1, string.Empty);
		Assert.Equal(new SessionContentLastUsedModel(null, null, null), store.GetLastUsed(File1));
	}

	[Fact]
	public void The_profile_comes_from_the_last_user_entry_and_model_with_effort_from_the_last_assistant_entry_with_a_model()
	{
		var text = EntryText("USER", 0, "u0", "profile=\"first\"")
			+ EntryText("ASSISTANT", 1, "a1", "model=\"opus\" effort=\"high\"")
			+ EntryText("USER", 2, "u2", "profile=\"second\"")
			+ EntryText("ASSISTANT", 3, "a3", "model=\"sonnet\"")
			+ EntryText("ASSISTANT", 4, "a4")
			+ EntryText("USER", 5, "u5");
		WriteRaw(File1, text);
		using var store = NewStore();

		var used = store.GetLastUsed(File1);

		Assert.Equal(new SessionContentLastUsedModel("second", "sonnet", null), used);
		AssertAgrees(store, File1);
	}

	[Fact]
	public void Metadata_on_the_wrong_role_is_ignored_like_the_resolver_ignores_it()
	{
		var text = EntryText("SYSTEM", 0, "s", "profile=\"sys\" model=\"haiku\" effort=\"low\"")
			+ EntryText("USER", 1, "u", "model=\"opus\" effort=\"max\"")
			+ EntryText("ASSISTANT", 2, "a", "profile=\"asst\"");
		WriteRaw(File1, text);
		using var store = NewStore();

		Assert.Equal(new SessionContentLastUsedModel(null, null, null), store.GetLastUsed(File1));
		AssertAgrees(store, File1);
	}

	[Fact]
	public void It_follows_appends_rewrites_and_a_restart()
	{
		WriteConversation(File1, 4);
		using (var store = NewStore())
		{
			AssertAgrees(store, File1);

			AppendRaw(File1, EntryText("ASSISTANT", 10, "late", "model=\"haiku\" effort=\"low\""));
			AssertAgrees(store, File1);
			Assert.Equal("haiku", store.GetLastUsed(File1).ModelId);

			WriteRaw(File1, EntryText("USER", 0, "rewritten", "profile=\"new\""));
			AssertAgrees(store, File1);
			Assert.Equal("new", store.GetLastUsed(File1).ProfileName);
		}

		using var restarted = NewStore();
		AssertAgrees(restarted, File1);
	}

	[Fact]
	public void A_huge_entry_carrying_the_metadata_is_read_cut_to_its_header_line()
	{
		var big = new string('q', 600_000);
		var sb = new StringBuilder();
		sb.Append(EntryText("USER", 0, big, "profile=\"big\""));
		sb.Append(EntryText("ASSISTANT", 1, big, "model=\"opus\" effort=\"high\""));
		WriteRaw(File1, sb.ToString());
		using var store = NewStore();

		Assert.Equal(new SessionContentLastUsedModel("big", "opus", "high"), store.GetLastUsed(File1));
	}

	[Fact]
	public void Header_edge_cases_agree_with_a_full_parse()
	{
		var text = "[2026-11-14T08:00:00Z] USER profile=\"w\"\n[x] not a header\n[2026-11-14T08:01:00Z] ASSISTANT model=\"opus\" effort=\"high\"\n\n[ ]\n[bad] USER profile=\"no\"\n"
			+ EntryText("USER", 5, "[link](http://a)", "profile=\"later\"");
		WriteRaw(File1, text);
		using var store = NewStore();

		AssertAgrees(store, File1);
	}
}
