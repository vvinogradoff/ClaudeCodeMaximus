using System.Diagnostics;
using System.Text.Json;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// Input selection and keyboard navigation of the transcript page (<c>Web/inputnav.js</c>) under node: which entry an Up/Down move reaches from a
/// selected input of mine or an automated one, what the keys do with nothing selected, and where a revealed input is scrolled to. Needs
/// <c>node</c> on PATH, like <see cref="PagingScriptTests"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class InputNavScriptTests
{
	private static readonly string File = Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, "inputnav.js");

	// Entries 0..9: mine(0) A(1) scheduled(2) A(3) mine(4) S(5) orchestrated(6) A(7) web-mine(8) telegram-mine(9)
	private const string Items = @"
		const it=(i,r,s)=>({i:i,r:r,s:s||''});
		const items=[it(0,'U'),it(1,'A'),it(2,'U','scheduled'),it(3,'A'),it(4,'U'),it(5,'S'),it(6,'U','orchestrated'),it(7,'A'),it(8,'U','web'),it(9,'U','telegram')];
		const walk=(from,dir,mine)=>{const seen=[];let i=from;for(;;){const k=n.find(items,i,dir,mine);if(k<0)break;i=items[k].i;seen.push(i);}return seen;};";

	private static JsonElement Run(string body)
	{
		Assert.True(System.IO.File.Exists(File), "inputnav.js was not copied next to the test assembly: " + File);
		var script = $"const n=require({JsonSerializer.Serialize(File)});{Items}const out=[];{body};process.stdout.write(JSON.stringify(out));";
		var info = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		info.ArgumentList.Add("-e");
		info.ArgumentList.Add(script);
		using var process = Process.Start(info) ?? throw new InvalidOperationException("node could not be started (it must be on PATH)");
		var output = process.StandardOutput.ReadToEnd();
		var error = process.StandardError.ReadToEnd();
		Assert.True(process.WaitForExit(20_000), "node did not finish");
		Assert.True(process.ExitCode == 0, "node failed: " + error);
		return JsonDocument.Parse(output).RootElement.Clone();
	}

	private static int[] Ints(JsonElement e) => e.EnumerateArray().Select(x => x.GetInt32()).ToArray();

	[Fact]
	public void Scheduled_and_orchestrated_inputs_are_automated_and_labelled_everything_else_is_mine()
	{
		var result = Run("out.push(items.map(x => n.isInput(x) ? (n.isAuto(x) ? 'auto' : 'mine') : '-'), items.map(x => n.labelOf(x)));");

		Assert.Equal(["mine", "-", "auto", "-", "mine", "-", "auto", "-", "mine", "mine"], result[0].EnumerateArray().Select(x => x.GetString()).ToArray());
		Assert.Equal([null, null, "SCHEDULED", null, null, null, "SUPERVISOR", null, null, null],
			result[1].EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? null : x.GetString()).ToArray());
	}

	[Fact]
	public void From_one_of_mine_the_arrows_visit_my_inputs_only()
	{
		var result = Run("out.push(walk(0, 1, true), walk(9, -1, true));");

		Assert.Equal([4, 8, 9], Ints(result[0]));
		Assert.Equal([8, 4, 0], Ints(result[1]));
	}

	[Fact]
	public void From_an_automated_input_the_arrows_visit_every_input()
	{
		var result = Run("out.push(walk(2, 1, false), walk(6, -1, false));");

		Assert.Equal([4, 6, 8, 9], Ints(result[0]));
		Assert.Equal([4, 2, 0], Ints(result[1]));
	}

	[Fact]
	public void Find_returns_a_position_in_the_list_and_works_when_the_list_starts_later()
	{
		// Entries 4..9 loaded (positions 0..5): from 4 the next input is 6 at position 2; nothing before 4 is loaded; from 2 (not loaded) mine is 4.
		var result = Run("const tail = items.slice(4); out.push(n.find(tail, 4, 1, false), n.find(tail, 4, -1, false), n.find(tail, 9, 1, false), n.find(tail, 2, 1, true), n.find([], 0, 1, false));");

		Assert.Equal([2, -1, -1, 0, -1], Ints(result));
	}

	[Fact]
	public void Find_from_an_entry_that_is_not_an_input_still_works_on_entry_indexes()
	{
		var result = Run("out.push(n.find(items, 3, 1, false), n.find(items, 3, -1, false), n.find(items, 5, 1, true), n.find(items, 5, -1, true));");

		Assert.Equal([4, 2, 8, 4], Ints(result));
	}

	[Fact]
	public void With_nothing_selected_the_arrows_scroll_a_line_and_PageUp_PageDown_a_page()
	{
		var result = Run("const a = (k, sel, mods) => n.keyAction(k, mods || {}, sel, 800); out.push(a('ArrowDown', false), a('ArrowUp', false), a('PageDown', false), a('PageUp', false), a('Escape', false), a('x', false));");

		Assert.Equal(("scroll", 40), (result[0].GetProperty("type").GetString(), result[0].GetProperty("dy").GetInt32()));
		Assert.Equal(-40, result[1].GetProperty("dy").GetInt32());
		Assert.Equal(700, result[2].GetProperty("dy").GetInt32());
		Assert.Equal(-700, result[3].GetProperty("dy").GetInt32());
		Assert.Equal(JsonValueKind.Null, result[4].ValueKind);
		Assert.Equal(JsonValueKind.Null, result[5].ValueKind);
	}

	[Fact]
	public void With_an_input_selected_the_arrows_navigate_Escape_clears_and_the_page_keys_still_scroll()
	{
		var result = Run("const a = (k) => n.keyAction(k, {}, true, 800); out.push(a('ArrowDown'), a('ArrowUp'), a('Escape'), a('PageDown'));");

		Assert.Equal(("nav", 1), (result[0].GetProperty("type").GetString(), result[0].GetProperty("dir").GetInt32()));
		Assert.Equal(("nav", -1), (result[1].GetProperty("type").GetString(), result[1].GetProperty("dir").GetInt32()));
		Assert.Equal("clear", result[2].GetProperty("type").GetString());
		Assert.Equal(("scroll", 700), (result[3].GetProperty("type").GetString(), result[3].GetProperty("dy").GetInt32()));
	}

	[Fact]
	public void Keys_with_modifiers_are_left_alone()
	{
		var result = Run("for (const m of ['ctrl', 'alt', 'meta', 'shift']) { const mods = {}; mods[m] = true; out.push(n.keyAction('ArrowDown', mods, true, 800), n.keyAction('PageUp', mods, false, 800)); }");

		Assert.All(result.EnumerateArray(), e => Assert.Equal(JsonValueKind.Null, e.ValueKind));
	}

	[Fact]
	public void A_small_viewport_still_pages_by_at_least_a_line()
	{
		var result = Run("out.push(n.keyAction('PageDown', {}, false, 20));");

		Assert.Equal(40, result[0].GetProperty("dy").GetInt32());
	}

	[Fact]
	public void A_revealed_input_sits_a_margin_below_the_top_and_never_above_zero()
	{
		var result = Run("const off=[0,100,260,400]; out.push(n.revealTop(off,2,24), n.revealTop(off,0,24), n.revealTop(off,1,24));");

		Assert.Equal([236, 0, 76], Ints(result));
	}
}
