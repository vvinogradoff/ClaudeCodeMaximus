using System.Diagnostics;
using System.Text.Json;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The live row of the transcript page (<c>Web/live.js</c>) under node with a minimal fake document: the timer text, the cut of long text, that
/// streamed model output is only ever set as text, and that the row is updated in place (a growing text is appended to its text node, so a
/// selection survives). Needs <c>node</c> on PATH, like <see cref="PagingScriptTests"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LiveScriptTests
{
	private static readonly string File = Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, "live.js");

	private const string FakeDocument = @"
		const mkNode = (tag) => ({ tag, className: '', children: [], style: {}, dataset: {}, _text: '',
			get firstChild() { return this.children[0] || null; }, get lastChild() { return this.children[this.children.length - 1] || null; },
			appendChild(c) { this.children.push(c); return c; }, removeChild(c) { this.children.splice(this.children.indexOf(c), 1); }, replaceChildren() { this.children = []; },
			get textContent() { return this._text !== '' ? this._text : this.children.map(c => c.data !== undefined ? c.data : c.textContent).join(''); },
			set textContent(v) { this._text = v; this.children = []; },
			set innerHTML(v) { throw new Error('innerHTML must never be used'); } });
		const doc = { createElement: mkNode, createTextNode: (t) => ({ tag: '#text', data: t, appends: 0, appendData(s) { this.data += s; this.appends++; } }) };
		const flat = (n) => [n].concat(...(n.children || []).map(flat));
		const find = (root, cls) => flat(root).find(n => n.className === cls);";

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
	public void Update_WithNothingToShow_HidesTheRow()
	{
		var result = Run("const v = L.create(doc); out.push(v.update(null, 0), v.el.style.display, v.update({ tools: [], queued: [] }, 0), v.el.style.display);");

		Assert.False(result[0].GetBoolean());
		Assert.Equal("none", result[1].GetString());
		Assert.False(result[2].GetBoolean());
		Assert.Equal("none", result[3].GetString());
	}

	[Fact]
	public void Update_ShowsStatusWithTimer_ToolsQueuedTextAndError_AllAsText()
	{
		var result = Run(@"
			const v = L.create(doc);
			out.push(v.update({ status: 'Thinking...', startedAt: 1000, usage: '5 in / 6 out', text: '<img src=x onerror=alert(1)>', thinking: 'hmm',
				tools: [{ id: 'u', name: 'Bash', summary: 'ls', done: true, isError: true }], queued: ['next prompt'], error: 'it failed' }, 8000));
			out.push(find(v.el, 'lt').textContent, find(v.el, 'ltx').textContent, find(v.el, 'ltool done err').textContent, find(v.el, 'lq').textContent,
				find(v.el, 'lerr').textContent, find(v.el, 'lk').textContent, find(v.el, 'ls').textContent, v.startAt, v.el.style.display);");

		var values = result.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()).ToArray();
		Assert.Equal("True", values[0]);
		Assert.Equal("7s", values[1]);
		Assert.Equal("<img src=x onerror=alert(1)>", values[2]);   // text, not markup
		Assert.Equal("Bash: ls  [failed]", values[3]);
		Assert.Equal("Queued: next prompt", values[4]);
		Assert.Equal("it failed", values[5]);
		Assert.Equal("hmm", values[6]);
		Assert.Equal("Thinking... - 5 in / 6 out ", values[7]);
		Assert.Equal("1000", values[8]);
		Assert.Equal(string.Empty, values[9]);
	}

	[Fact]
	public void Update_GrowingText_IsAppendedToTheSameTextNode_SoASelectionSurvives()
	{
		var result = Run(@"
			const v = L.create(doc);
			v.update({ status: 'Responding...', startedAt: 1, text: 'Hello' }, 2000);
			const holder = find(v.el, 'ltx'), node = holder.firstChild, row = find(v.el, 'lh');
			v.update({ status: 'Responding...', startedAt: 1, text: 'Hello world' }, 3000);
			v.update({ status: 'Responding...', startedAt: 1, text: 'Hello world!!' }, 4000);
			out.push(holder.firstChild === node, node.data, node.appends, find(v.el, 'lh') === row);
			v.update({ status: 'Responding...', startedAt: 1, text: 'rewritten' }, 5000);   // not a growth: replaced in place
			out.push(holder.firstChild === node, node.data);");

		Assert.True(result[0].GetBoolean());
		Assert.Equal("Hello world!!", result[1].GetString());
		Assert.Equal(3, result[2].GetInt32());   // every growth is an append to the same node
		Assert.True(result[3].GetBoolean());
		Assert.True(result[4].GetBoolean());
		Assert.Equal("rewritten", result[5].GetString());
	}

	[Fact]
	public void Update_ToolRowsAreReusedByPosition_AndShrink()
	{
		var result = Run(@"
			const v = L.create(doc);
			const tool = (id, done) => ({ id, name: 'Read', summary: id, done, isError: false });
			v.update({ status: 's', tools: [tool('a', false), tool('b', false)] }, 0);
			const first = find(v.el, 'ltool');
			v.update({ status: 's', tools: [tool('a', true), tool('b', false)] }, 0);
			out.push(find(v.el, 'ltool done') === first, flat(v.el).filter(n => (n.className || '').startsWith('ltool ') || n.className === 'ltool').length);
			v.update({ status: 's', tools: [tool('a', true)] }, 0);
			out.push(flat(v.el).filter(n => (n.className || '').startsWith('ltool ') || n.className === 'ltool').length);");

		Assert.True(result[0].GetBoolean());
		Assert.Equal(2, result[1].GetInt32());
		Assert.Equal(1, result[2].GetInt32());
	}

	[Fact]
	public void Update_WithoutAStartTime_HasNoTimer_AndClearingHidesTheRowAgain()
	{
		var result = Run(@"
			const v = L.create(doc);
			v.update({ status: 'Waiting in the queue', queued: ['x'] }, 5);
			out.push(find(v.el, 'lt').style.display, v.startAt === null);
			out.push(v.update(null, 6), v.el.style.display);");

		Assert.Equal("none", result[0].GetString());
		Assert.True(result[1].GetBoolean());
		Assert.False(result[2].GetBoolean());
		Assert.Equal("none", result[3].GetString());
	}
}
