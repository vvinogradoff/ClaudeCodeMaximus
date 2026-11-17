using System.Diagnostics;
using System.Text.Json;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The scrollbar mapping of the transcript page (<c>Web/scrollmap.js</c>) under node: the thumb is proportional to the entry index, so a drag lands on the
/// entry the mouse points at whatever the estimated heights of the other entries are. Needs <c>node</c> on PATH, like <see cref="PagingScriptTests"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScrollMapScriptTests
{
	private static readonly string File = Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, "scrollmap.js");

	// 10 entries; heights 100, 20, 300, 40, 40, 40, 500, 60, 60, 40 (total 1200); a 400 px view scrolls at most 800.
	private const string Setup = @"
		const heights=[100,20,300,40,40,40,500,60,60,40];
		const off=new Float64Array(heights.length+1);heights.forEach((h,k)=>off[k+1]=off[k]+h);
		const max=off[10]-400;";

	private static JsonElement Run(string body)
	{
		Assert.True(System.IO.File.Exists(File), "scrollmap.js was not copied next to the test assembly: " + File);
		var script = $"const m=require({JsonSerializer.Serialize(File)});{Setup}const out=[];{body};process.stdout.write(JSON.stringify(out));";
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

	private static void AssertClose(double[] expected, double[] actual)
	{
		Assert.Equal(expected.Length, actual.Length);
		for (var i = 0; i < expected.Length; i++)
			Assert.Equal(expected[i], actual[i], 6);
	}

	private static double[] Doubles(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();

	[Fact]
	public void Entry_position_is_the_entry_index_plus_the_part_of_it_scrolled_past()
	{
		var result = Run("out.push(m.entryPos(off, 0), m.entryPos(off, 50), m.entryPos(off, 100), m.entryPos(off, 250), m.entryPos(off, 1200));");

		AssertClose([0, 0.5, 1, 2.0 + 130.0 / 300.0, 10], Doubles(result));
	}

	[Fact]
	public void The_thumb_is_at_the_ends_exactly_at_the_ends_of_the_scroll_range()
	{
		var result = Run("out.push(m.fractionOf(off, 0, max), m.fractionOf(off, max, max), m.scrollForFraction(off, 0, max), m.scrollForFraction(off, 1, max));");

		Assert.Equal([0, 1, 0, 800], Doubles(result));
	}

	[Fact]
	public void The_thumb_position_and_the_scroll_offset_map_back_and_forth()
	{
		var result = Run("const r=[];for(let y=0;y<=max;y+=7){const f=m.fractionOf(off,y,max);r.push(Math.abs(m.scrollForFraction(off,f,max)-y));}out.push(Math.max(...r));");

		Assert.True(Doubles(result)[0] < 1e-6, "round trip error " + Doubles(result)[0]);
	}

	[Fact]
	public void The_thumb_moves_with_the_entry_index_not_with_the_pixels()
	{
		// Entry 6 is 500 px of the 1200; a thumb dragged evenly through the travel passes the entries at an even pace in entry space, so the
		// position in entries advances by the same amount per step even while the pixels per step vary (big entry: 500 px, small ones: 20 px).
		var result = Run("const t=[];for(let s=0;s<=10;s++)t.push(m.entryPos(off,m.scrollForFraction(off,s/10,max)));out.push(...t.slice(1).map((x,i)=>x-t[i]));");

		var steps = Doubles(result);
		Assert.All(steps, s => Assert.Equal(steps[0], s, 6));
	}

	[Fact]
	public void The_position_is_monotonic_in_the_thumb()
	{
		var result = Run("let prev=-1,ok=true;for(let i=0;i<=1000;i++){const y=m.scrollForFraction(off,i/1000,max);if(y<prev)ok=false;prev=y;}out.push(ok);");

		Assert.True(result[0].GetBoolean());
	}

	[Fact]
	public void Remeasuring_entries_below_the_top_one_does_not_move_a_thumb_that_is_dragged_to_a_given_entry()
	{
		// Dragged to fraction f the top of the view is entry t = f * tMax; the heights of the entries BELOW the top entry change (they are measured as they come
		// into view) and the entry at the top of the view is the same one, at the same fraction.
		var result = Run(@"
			const f=0.4;
			const before=m.entryPos(off,m.scrollForFraction(off,f,max));
			const off2=Float64Array.from(off);for(let k=5;k<=10;k++)off2[k]+=(k-4)*90;   // entries 4..9 turned out taller
			const max2=off2[10]-400;
			const after=m.entryPos(off2,m.scrollForFraction(off2,f,max2));
			out.push(Math.floor(before),Math.floor(after));");

		Assert.Equal(Doubles(result)[0], Doubles(result)[1]);
	}

	[Fact]
	public void Thumb_length_is_the_visible_share_with_a_minimum_and_fills_the_track_when_everything_fits()
	{
		var result = Run("out.push(m.thumbLength(800, 400, 1200), m.thumbLength(800, 400, 400000), m.thumbLength(800, 400, 300), m.thumbLength(0, 400, 1200), m.thumbLength(800, 400, 0));");

		AssertClose([800.0 / 3, 28, 800, 0, 800], Doubles(result));
	}

	[Fact]
	public void No_scroll_range_means_the_thumb_stays_at_the_top()
	{
		var result = Run("out.push(m.fractionOf(off, 0, 0), m.scrollForFraction(off, 0.7, 0), m.fractionOf(new Float64Array(1), 0, 0), m.scrollOf(new Float64Array(1), 3));");

		Assert.Equal([0, 0, 0, 0], Doubles(result));
	}
}
