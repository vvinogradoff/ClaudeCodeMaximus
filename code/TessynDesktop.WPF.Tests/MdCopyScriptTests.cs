using System.Diagnostics;
using System.Text.Json;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The Copy as Markdown converter of the transcript page (<c>Web/mdcopy.js</c>, FR.2.13) under Node: the HTML the real renderer produces for a piece of
/// Markdown is converted back (TC.CM.01, TC.CM.06), and the elements the page itself adds - path chips, image slots, media previews - are converted too
/// (TC.CM.03). Node has no DOM, so a small HTML reader (below) builds the few node members the converter reads. Needs <c>node</c> on PATH.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MdCopyScriptTests
{
	private static readonly string File = Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, "mdcopy.js");

	private readonly MarkdigEntryRenderer _renderer = new(new UrlPolicy());

	/// <summary>A reader for the well-formed HTML of the renderer and the page: elements, attributes, text with the common entities, void tags.</summary>
	private const string MiniDom = """
		const VOID = new Set(['br', 'hr', 'img']);
		const decode = s => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&');
		function parse(html) {
		  const root = { nodeType: 11, nodeName: '#document-fragment', childNodes: [], getAttribute() { return null; } };
		  const stack = [root];
		  const re = /<\/([a-zA-Z0-9]+)\s*>|<([a-zA-Z0-9]+)((?:\s+[a-zA-Z-]+(?:="[^"]*")?)*)\s*\/?>|([^<]+)/g;
		  let m;
		  while ((m = re.exec(html))) {
		    const top = stack[stack.length - 1];
		    if (m[4] !== undefined) top.childNodes.push({ nodeType: 3, nodeValue: decode(m[4]) });
		    else if (m[1]) stack.pop();
		    else {
		      const attrs = {}; const are = /([a-zA-Z-]+)(?:="([^"]*)")?/g; let a;
		      while ((a = are.exec(m[3] || ''))) attrs[a[1]] = decode(a[2] === undefined ? '' : a[2]);
		      const el = { nodeType: 1, nodeName: m[2].toUpperCase(), childNodes: [], getAttribute(n) { return n in attrs ? attrs[n] : null; } };
		      top.childNodes.push(el);
		      if (!VOID.has(m[2].toLowerCase())) stack.push(el);
		    }
		  }
		  return root;
		}
		""";

	private static string Convert(string html, string media = "() => null")
	{
		Assert.True(System.IO.File.Exists(File), "mdcopy.js was not copied next to the test assembly: " + File);
		var script = $"const g=require({JsonSerializer.Serialize(File)});{MiniDom}process.stdout.write(JSON.stringify(g.fromNode(parse({JsonSerializer.Serialize(html)}), {{ media: {media} }})));";
		var info = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		info.ArgumentList.Add("-e");
		info.ArgumentList.Add(script);
		using var process = Process.Start(info) ?? throw new InvalidOperationException("node could not be started (it must be on PATH)");
		var output = process.StandardOutput.ReadToEnd();
		var error = process.StandardError.ReadToEnd();
		Assert.True(process.WaitForExit(20_000), "node did not finish");
		Assert.True(process.ExitCode == 0, "node failed: " + error);
		return JsonDocument.Parse(output).RootElement.GetString()!;
	}

	/// <summary>Markdown -> the HTML the page would show for an entry -> the converter's Markdown.</summary>
	private string RoundTrip(string markdown, string role = "assistant") =>
		Convert(_renderer.Render(new SessionEntryDto { Index = 1, Timestamp = DateTimeOffset.UtcNow, Role = role, Content = markdown }).Html);

	[Theory]
	[InlineData("# One\n\n## Two\n\n### Three", "# One\n\n## Two\n\n### Three")]
	[InlineData("Some **bold**, some *italic* and ~~gone~~ text.", "Some **bold**, some *italic* and ~~gone~~ text.")]
	[InlineData("Use `dotnet build` now.", "Use `dotnet build` now.")]
	[InlineData("Use ``a ` b`` now.", "Use ``a ` b`` now.")]
	[InlineData("> quoted line\n> second line", "> quoted line second line")]   // the page shows a soft line break as a space
	[InlineData("> first\n>\n> second", "> first\n>\n> second")]
	[InlineData("see [the docs](https://example.com/docs) end", "see [the docs](https://example.com/docs) end")]
	[InlineData("a bare https://example.com/x link", "a bare https://example.com/x link")]
	public void TC_CM_01_Inline_formatting_headings_quotes_and_links_come_back_as_Markdown(string markdown, string expected)
	{
		Assert.Equal(expected, RoundTrip(markdown));
	}

	[Fact]
	public void TC_CM_01_A_fenced_code_block_keeps_its_language_and_its_text_exactly()
	{
		var markdown = "```csharp\nvar a = 1;\n\n  if (a < 2) { }\n```";

		Assert.Equal(markdown, RoundTrip(markdown));
	}

	[Fact]
	public void TC_CM_01_A_code_block_without_a_language_is_fenced_longer_than_any_fence_inside_it()
	{
		Assert.Equal("```\nplain\n```", RoundTrip("```\nplain\n```"));
		Assert.Equal("````\n```\ninner\n```\n````", RoundTrip("````\n```\ninner\n```\n````"));
	}

	[Fact]
	public void TC_CM_01_Bulleted_and_numbered_lists_nest_with_their_markers()
	{
		var markdown = "- one\n  - inner a\n  - inner b\n- two\n\n1. first\n2. second\n   1. nested";

		Assert.Equal("- one\n  - inner a\n  - inner b\n- two\n\n1. first\n2. second\n   1. nested", RoundTrip(markdown));
	}

	[Fact]
	public void TC_CM_01_A_numbered_list_keeps_its_start()
	{
		Assert.Equal("3. three\n4. four", RoundTrip("3. three\n4. four"));
	}

	[Fact]
	public void TC_CM_01_A_loose_list_keeps_its_blank_lines()
	{
		Assert.Equal("- one\n\n- two", RoundTrip("- one\n\n- two"));
	}

	[Fact]
	public void TC_CM_01_A_table_keeps_its_rows_and_alignments()
	{
		var markdown = "| Name | Qty | Note |\n| :--- | ---: | :---: |\n| a | 1 | x \\| y |\n| b | 22 | |";

		Assert.Equal("| Name | Qty | Note |\n| :--- | ---: | :---: |\n| a | 1 | x \\| y |\n| b | 22 |  |", RoundTrip(markdown));
	}

	[Fact]
	public void TC_CM_01_Both_rules_and_line_breaks_come_back()
	{
		Assert.Equal("above\n\n---\n\nbelow\n\n===\n\nlast", RoundTrip("above\n\n---\n\nbelow\n\n===\n\nlast"));
		Assert.Equal("first  \nsecond", RoundTrip("first  \nsecond"));
	}

	[Fact]
	public void TC_CM_01_A_whole_response_with_every_kind_of_block_keeps_the_order_and_a_blank_line_between_blocks()
	{
		var markdown = "## Plan\n\nText with `code`.\n\n- item\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n\n```sh\nls\n```";

		Assert.Equal(markdown, RoundTrip(markdown));
	}

	[Theory]
	[InlineData("user")]
	[InlineData("assistant")]
	[InlineData("system")]
	public void TC_CM_06_Every_row_kind_converts_the_same(string role)
	{
		Assert.Equal("## Head\n\n- a\n- b\n\n`x`", RoundTrip("## Head\n\n- a\n- b\n\n`x`", role));
	}

	[Fact]
	public void TC_CM_06_Plain_text_with_line_breaks_stays_readable()
	{
		// A paragraph the user typed: Markdig keeps a soft line break as a newline in the text, which HTML shows as one space.
		Assert.Equal("one two", RoundTrip("one\ntwo", "user"));
	}

	[Fact]
	public void TC_CM_03_A_path_chip_copies_as_its_text_and_an_image_under_it_as_its_Markdown_link()
	{
		const string Html = "<p>see <span class=\"pth\" data-j=\"0\">C:\\a\\x.png</span><span class=\"pslot\" data-j=\"0\"><img class=\"pimg\" src=\"https://preview.local/c/1.png\" alt=\"x.png\" data-i=\"1\" data-j=\"0\"></span> end</p>";

		var markdown = Convert(Html, "el => el.getAttribute('data-j') === '0' ? { url: 'C:\\\\a\\\\x.png', alt: 'x.png', video: false } : null");

		Assert.Equal("see C:\\a\\x.png ![x.png](C:\\a\\x.png) end", markdown);
	}

	[Fact]
	public void TC_CM_03_A_path_with_spaces_is_written_in_angle_brackets_and_a_video_is_a_plain_link()
	{
		const string Html = "<p><span class=\"pslot\" data-j=\"0\"><span class=\"pvid\"><img class=\"pimg\" data-j=\"0\"><span class=\"pplay\"></span></span></span></p>";

		var markdown = Convert(Html, "() => ({ url: 'C:\\\\my clips\\\\a.mp4', alt: 'a.mp4', video: true })");

		Assert.Equal("[a.mp4](<C:\\my clips\\a.mp4>)", markdown);
	}

	[Fact]
	public void TC_CM_03_A_hint_in_an_image_slot_is_not_copied()
	{
		const string Html = "<p>see <span class=\"pth\">C:\\a\\x.png</span><span class=\"pslot\" data-j=\"0\"><span class=\"pnote\">Loading image&hellip;</span></span></p>";

		Assert.Equal("see C:\\a\\x.png", Convert(Html));
	}

	[Fact]
	public void TC_CM_03_Media_previews_copy_as_one_link_each_loaded_or_not()
	{
		const string Html = "<div class=\"b\"><p>pics</p></div><div class=\"mp\"><div class=\"ms\" data-j=\"0\"><img class=\"mimg\" src=\"https://media.local/i?u=x\"></div><div class=\"ms\" data-j=\"1\"><button class=\"mload\" type=\"button\">Load preview cdn.example.com</button></div></div>";

		var markdown = Convert(Html, "el => ({ url: 'https://cdn.example.com/' + el.getAttribute('data-j') + (el.getAttribute('data-j') === '1' ? '.mp4' : '.png'), alt: '', video: el.getAttribute('data-j') === '1' })");

		Assert.Equal("pics\n\n![](https://cdn.example.com/0.png)\n\n[](https://cdn.example.com/1.mp4)", markdown);
	}

	[Fact]
	public void TC_CM_03_A_link_the_policy_refused_is_plain_text_and_row_chrome_is_left_out()
	{
		const string Html = "<div class=\"e A\"><div class=\"c\"><div class=\"h\">model</div><div class=\"b\"><p>see <a class=\"blocked\" href=\"#\">this</a> and <a href=\"https://example.com/\">that</a></p></div><a class=\"more\" data-i=\"1\">Show full entry (900 chars)</a></div><div class=\"t\">12:00:00</div></div>";

		Assert.Equal("see this and [that](https://example.com/)", Convert(Html));
	}

	[Fact]
	public void A_fragment_cut_in_the_middle_of_a_list_and_a_table_still_converts()
	{
		// What a selection's cloned contents look like: a list with only some of its items, a row without its header.
		Assert.Equal("- b\n- c", Convert("<ul><li>b</li><li>c</li></ul>"));
		Assert.Equal("just text", Convert("just text"));
		Assert.Equal("", Convert(""));
	}
}
