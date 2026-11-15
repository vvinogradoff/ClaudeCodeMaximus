using System.Diagnostics;
using System.Text.Json;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The transcript page's newer-page request rule (<c>Web/paging.js</c>) under node: after a failed request the page does not ask again until
/// the user scrolls (N3). Needs <c>node</c> on PATH, like <see cref="MediaGateScriptTests"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PagingScriptTests
{
	private static readonly string File = Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, "paging.js");

	private static JsonElement Run(string body)
	{
		Assert.True(System.IO.File.Exists(File), "paging.js was not copied next to the test assembly: " + File);
		var script = $"const g=require({JsonSerializer.Serialize(File)});const gate=g.create();const out=[];const ask=(top)=>g.shouldRequest(gate,true,top,600,2000,300);{body};process.stdout.write(JSON.stringify(out));";
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

	[Fact]
	public void AFailedRequest_IsNotRepeatedUntilTheUserScrolls()
	{
		// The view is at the end (scrollTop 1100 + 600 >= 2000 - 300). Request, fail, then the page re-checks (as it does after every render).
		var result = Run(@"
			out.push(ask(1100));                      // 0: near the end, nothing pending: request
			g.markRequested(gate);
			out.push(ask(1100));                      // 1: one at a time
			g.markFailed(gate, 1100);
			out.push(ask(1100));                      // 2: failed, same position: no request (this was the loop)
			g.onUserScroll(gate, 1100);
			out.push(ask(1100));                      // 3: the render's own check did not move the view: still no request
			g.onUserScroll(gate, 1120);               // the user scrolled
			out.push(ask(1120));                      // 4: may try again
			g.markRequested(gate); g.markLoaded(gate);
			out.push(ask(1120));                      // 5: loaded: asks again while near the end
			out.push(ask(100));                       // 6: far from the end: no
		");

		Assert.Equal([true, false, false, false, true, true, false], result.EnumerateArray().Select(e => e.GetBoolean()).ToArray());
	}

	[Fact]
	public void WithNothingNewer_NeverRequests()
	{
		var result = Run("out.push(g.shouldRequest(gate,false,1100,600,2000,300));");

		Assert.False(result[0].GetBoolean());
	}
}
