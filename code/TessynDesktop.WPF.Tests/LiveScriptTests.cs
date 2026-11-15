using System.Diagnostics;
using System.Text.Json;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The live row of the transcript page (<c>Web/live.js</c>) under node with a minimal fake document: the timer text, the cut of long text and that
/// streamed model output is only ever set as text. Needs <c>node</c> on PATH, like <see cref="PagingScriptTests"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LiveScriptTests
{
	private static readonly string File = Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, "live.js");

	private const string FakeDocument = @"
		const mk = (tag) => ({ tag, className: '', textContent: '', children: [], attrs: {}, appendChild(c) { this.children.push(c); return c; }, setAttribute(k, v) { this.attrs[k] = v; },
			get html() { return this.innerHTML; }, set innerHTML(v) { throw new Error('innerHTML must never be used'); } });
		const doc = { createElement: mk, createTextNode: (t) => ({ tag: '#text', textContent: t, children: [] }) };
		const flat = (n) => [n].concat(...(n.children || []).map(flat));";

	private static JsonElement Run(string body)
	{
		Assert.True(System.IO.File.Exists(File), "live.js was not copied next to the test assembly: " + File);
		var script = $"const L=require({JsonSerializer.Serialize(File)});{FakeDocument}const out=[];{body};process.stdout.write(JSON.stringify(out));";
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
	public void ElapsedText_CountsSecondsThenMinutes()
	{
		var result = Run("out.push(L.elapsedText(0), L.elapsedText(7400), L.elapsedText(59999), L.elapsedText(65000), L.elapsedText(3725000), L.elapsedText(-5));");

		Assert.Equal(["0s", "7s", "59s", "1m 05s", "62m 05s", "0s"], result.EnumerateArray().Select(e => e.GetString()).ToArray());
	}

	[Fact]
	public void Tail_KeepsTheNewestPart_AndMarksTheCut()
	{
		var result = Run("out.push(L.tail('short', 10), L.tail('a'.repeat(50) + String.fromCharCode(10) + 'newest line', 20), L.tail('x'.repeat(100), 10), L.tail(null, 10));");

		var values = result.EnumerateArray().Select(e => e.GetString()).ToArray();
		Assert.Equal("short", values[0]);
		Assert.Equal("...\nnewest line", values[1]);
		Assert.Equal("..." + new string('x', 10), values[2]);
		Assert.Equal(string.Empty, values[3]);
	}

	[Fact]
	public void Build_WithNothingToShow_ReturnsNull()
	{
		var result = Run("out.push(L.build(doc, null, 0) === null, L.build(doc, { tools: [], queued: [] }, 0) === null);");

		Assert.True(result[0].GetBoolean());
		Assert.True(result[1].GetBoolean());
	}

	[Fact]
	public void Build_ShowsStatusWithTimer_ToolsQueuedTextAndError_AllAsText()
	{
		var result = Run(@"
			const row = L.build(doc, { status: 'Thinking...', startedAt: 1000, usage: '5 in / 6 out', text: '<img src=x onerror=alert(1)>', thinking: 'hmm',
				tools: [{ id: 'u', name: 'Bash', summary: 'ls', done: true, isError: true }], queued: ['next prompt'], error: 'it failed' }, 8000);
			const nodes = flat(row);
			out.push(nodes.find(n => n.className === 'lt').textContent);
			out.push(nodes.find(n => n.className === 'lt').attrs['data-start']);
			out.push(nodes.find(n => n.className === 'ltx').textContent);
			out.push(nodes.find(n => (n.className || '').startsWith('ltool')).textContent);
			out.push(nodes.find(n => (n.className || '').startsWith('ltool')).className);
			out.push(nodes.find(n => n.className === 'lq').textContent);
			out.push(nodes.find(n => n.className === 'lerr').textContent);
			out.push(nodes.find(n => n.className === 'lk').textContent);");

		var values = result.EnumerateArray().Select(e => e.GetString()).ToArray();
		Assert.Equal("7s", values[0]);
		Assert.Equal("1000", values[1]);
		Assert.Equal("<img src=x onerror=alert(1)>", values[2]);   // text, not markup
		Assert.Equal("Bash: ls  [failed]", values[3]);
		Assert.Equal("ltool done err", values[4]);
		Assert.Equal("Queued: next prompt", values[5]);
		Assert.Equal("it failed", values[6]);
		Assert.Equal("hmm", values[7]);
	}

	[Fact]
	public void Build_WithoutAStartTime_HasNoTimer()
	{
		var result = Run("const row = L.build(doc, { status: 'Waiting in the queue', queued: ['x'] }, 5); out.push(flat(row).some(n => n.className === 'lt'));");

		Assert.False(result[0].GetBoolean());
	}
}
