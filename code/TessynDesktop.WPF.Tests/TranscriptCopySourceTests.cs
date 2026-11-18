using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Transcript;

namespace TessynDesktop.WPF.Tests;

/// <summary>TC.CM.02: a selection that covers whole entries copies their original Markdown, separated by a blank line; a partial selection keeps what the page converted.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TranscriptCopySourceTests
{
	private readonly MarkdigEntryRenderer _renderer = new(new UrlPolicy());
	private readonly TranscriptCopySource _source = new();

	private TranscriptEntryView Entry(int index, string content, bool truncated = false) =>
		_renderer.Render(new SessionEntryDto { Index = index, Timestamp = DateTimeOffset.UtcNow, Role = "assistant", Content = content, Truncated = truncated });

	[Fact]
	public void The_renderer_hands_the_original_markdown_to_the_view()
	{
		Assert.Equal("## A *title*\n\n- x", Entry(4, "## A *title*\n\n- x").SourceMarkdown);
	}

	[Fact]
	public void TC_CM_02_Two_whole_entries_copy_their_source_with_a_blank_line_between()
	{
		_source.Remember([Entry(1, "# One\n\n- a"), Entry(2, "Two **bold**")]);

		var text = _source.Compose([new CopyPart(1, true, "converted one"), new CopyPart(2, true, "converted two")], useSources: true);

		Assert.Equal("# One\n\n- a\n\nTwo **bold**", text);
	}

	[Fact]
	public void TC_CM_02_A_partial_part_keeps_the_converted_text_and_a_whole_one_beside_it_uses_the_source()
	{
		_source.Remember([Entry(1, "# One\n\n- a\n- b"), Entry(2, "Two **bold** end")]);

		var text = _source.Compose([new CopyPart(1, false, "- b"), new CopyPart(2, true, "Two bold end")], useSources: true);

		Assert.Equal("- b\n\nTwo **bold** end", text);
	}

	[Fact]
	public void A_truncated_entry_is_never_copied_from_its_cut_source()
	{
		_source.Remember([Entry(1, "cut text", truncated: true)]);

		Assert.Equal("converted", _source.Compose([new CopyPart(1, true, "converted")], useSources: true));
	}

	[Fact]
	public void Expanding_an_entry_replaces_its_source_and_a_load_forgets_every_source()
	{
		_source.Remember([Entry(1, "cut", truncated: true)]);
		_source.Remember([Entry(1, "the full text")]);
		Assert.Equal("the full text", _source.Compose([new CopyPart(1, true, "x")], useSources: true));

		_source.Reset();

		Assert.Equal("x", _source.Compose([new CopyPart(1, true, "x")], useSources: true));
	}

	[Fact]
	public void Parts_from_an_older_load_never_use_sources_and_empty_parts_are_dropped()
	{
		_source.Remember([Entry(7, "the source")]);

		Assert.Equal("converted\n\nlive", _source.Compose([new CopyPart(7, true, "converted"), new CopyPart(8, false, "  "), new CopyPart(null, false, "live")], useSources: false));
	}
}
