using System.Diagnostics;
using System.Text.Json;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The transcript page's inline local paths (<c>Web/pathslots.js</c>, FR.26.5) under Node: how an image slot turns into the preview below its
/// path, which states show which hint, and which clicks open a path or link (Ctrl + left button only). Needs <c>node</c> on PATH.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PathSlotsScriptTests
{
	private static readonly string File = Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, "pathslots.js");

	// The entry html as the host sends it, written as a JavaScript string literal.
	private const string Entry = "'<p>see <span class=\"pth\" data-j=\"0\">C:\\\\a\\\\x.png</span><span class=\"pslot\" data-j=\"0\"></span></p>'";
	private const string Paths = "['C:\\\\a\\\\x.png']";

	private static JsonElement Eval(string expression)
	{
		Assert.True(System.IO.File.Exists(File), "pathslots.js was not copied next to the test assembly: " + File);
		var script = $"const g=require({JsonSerializer.Serialize(File)});process.stdout.write(JSON.stringify({expression}));";
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

	private static string Fill(string state) => Eval($"g.fill({Entry}, 4, {Paths}, () => {state}).html").GetString()!;

	[Fact]
	public void ALoadedImage_AppearsInTheSlot_RightAfterThePath_WithItsOwnCacheUrlOnly()
	{
		var html = Fill("({ st: 'ok', src: 'https://preview.local/abc/content.png' })");

		Assert.Contains("<img class=\"pimg\" src=\"https://preview.local/abc/content.png\"", html);
		Assert.Contains("data-i=\"4\" data-j=\"0\"", html);
		Assert.True(html.IndexOf("class=\"pth\"", StringComparison.Ordinal) < html.IndexOf("<img", StringComparison.Ordinal));
		Assert.Contains("referrerpolicy=\"no-referrer\"", html);
	}

	[Theory]
	[InlineData("https://evil.example/x.png")]
	[InlineData("file:///C:/a/x.png")]
	[InlineData("data:image/png;base64,AAAA")]
	[InlineData("javascript:alert(1)")]
	public void AnImageSourceOutsideThePreviewCache_NeverBecomesAnImg(string src)
	{
		var html = Fill($"({{ st: 'ok', src: '{src}' }})");

		Assert.DoesNotContain("<img", html);
		Assert.DoesNotContain(src, html);
	}

	[Theory]
	[InlineData("({ st: 'denied' })", "Image not allowed")]
	[InlineData("({ st: 'fail' })", "Image unavailable")]
	[InlineData("({ st: 'wait' })", "Loading image")]
	public void TheSlotShowsAHintInsteadOfAnImage_WhileWaitingOrWhenRefusedOrFailed(string state, string hint)
	{
		var html = Fill(state);

		Assert.Contains(hint, html);
		Assert.DoesNotContain("<img", html);
	}

	private const string VideoEntry = "'<p>clip <span class=\"pth\" data-j=\"0\">C:\\\\a\\\\v.mp4</span><span class=\"pslot\" data-j=\"0\"></span></p>'";
	private const string VideoPaths = "['C:\\\\a\\\\v.mp4']";

	private static string FillVideo(string state) => Eval($"g.fill({VideoEntry}, 4, {VideoPaths}, () => {state}).html").GetString()!;

	[Fact]
	public void ALoadedVideoFrame_AppearsRightAfterThePath_WithAPlayBadgeOverIt()
	{
		var html = FillVideo("({ st: 'ok', src: 'https://preview.local/abc/content.png' })");

		Assert.Contains("<span class=\"pvid\"><img class=\"pimg\" src=\"https://preview.local/abc/content.png\"", html);
		Assert.Contains("data-i=\"4\" data-j=\"0\"", html);                  // a click lands on the frame, which opens it like the path
		Assert.Contains("<span class=\"pplay\" aria-hidden=\"true\"></span></span>", html);
		Assert.True(html.IndexOf("class=\"pth\"", StringComparison.Ordinal) < html.IndexOf("<img", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("({ st: 'denied' })", "Video not allowed")]
	[InlineData("({ st: 'fail' })", "Preview unavailable")]
	[InlineData("({ st: 'wait' })", "Loading preview")]
	public void AVideoSlotShowsItsOwnHints_NeverAnImage(string state, string hint)
	{
		var html = FillVideo(state);

		Assert.Contains(hint, html);
		Assert.DoesNotContain("<img", html);
		Assert.DoesNotContain("pplay", html);
	}

	[Theory]
	[InlineData("https://evil.example/x.png")]
	[InlineData("file:///C:/a/v.mp4")]
	[InlineData("https://preview.local.evil.example/x.png")]
	public void AVideoFrameFromOutsideThePreviewCache_NeverBecomesAnImg(string src)
	{
		var html = FillVideo($"({{ st: 'ok', src: '{src}' }})");

		Assert.DoesNotContain("<img", html);
		Assert.DoesNotContain("pplay", html);
	}

	[Theory]
	[InlineData("C:\\a\\v.mp4", true)]
	[InlineData("C:\\a\\v.MKV", true)]
	[InlineData("C:\\a\\v.wmv", true)]
	[InlineData("C:\\a\\v.png", false)]
	[InlineData("C:\\a\\v.mp4.txt", false)]
	[InlineData("C:\\a\\mp4", false)]
	public void TheScriptRecognisesVideoPathsByExtension(string path, bool expected) => Assert.Equal(expected, Eval($"g.isVideo({JsonSerializer.Serialize(path)})").GetBoolean());

	[Fact]
	public void APathNobodyAskedAbout_IsReportedAsWanted_OnceAnswered_Not()
	{
		var asked = Eval($"g.fill({Entry}, 4, {Paths}, () => null).wanted");
		var answered = Eval($"g.fill({Entry}, 4, {Paths}, () => ({{ st: 'fail' }})).wanted");

		Assert.Equal(["C:\\a\\x.png"], asked.EnumerateArray().Select(e => e.GetString()));
		Assert.Empty(answered.EnumerateArray());
	}

	[Fact]
	public void HtmlWithoutASlot_IsReturnedUntouched()
	{
		Assert.Equal("<p>no path</p>", Eval("g.fill('<p>no path</p>', 1, [], () => null).html").GetString());
		Assert.Equal("<p>x</p>", Eval($"g.fill('<p>x</p>', 1, {Paths}, () => null).html").GetString());
	}

	[Theory]
	[InlineData("{ type: 'click', button: 0, ctrlKey: true }", true)]
	[InlineData("{ type: 'click', button: 0, ctrlKey: false }", false)]            // a plain click only selects
	[InlineData("{ type: 'click', button: 0 }", false)]
	[InlineData("{ type: 'click', button: 0, ctrlKey: true, shiftKey: true }", false)]
	[InlineData("{ type: 'click', button: 0, ctrlKey: true, altKey: true }", false)]
	[InlineData("{ type: 'auxclick', button: 1, ctrlKey: true }", false)]          // only the left button
	[InlineData("{ type: 'click', button: 2, ctrlKey: true }", false)]
	[InlineData("{ type: 'mousedown', button: 0, ctrlKey: true }", false)]
	public void OnlyACtrlLeftClick_Opens(string ev, bool expected) => Assert.Equal(expected, Eval($"g.opens({ev})").GetBoolean());

	[Theory]
	[InlineData("{ type: 'click', button: 0, ctrlKey: true, shiftKey: true }", true)]     // FR.26.9: the forced system-browser gesture
	[InlineData("{ type: 'click', button: 0, ctrlKey: true }", false)]                    // Ctrl+click alone is the routed open
	[InlineData("{ type: 'click', button: 0, shiftKey: true }", false)]
	[InlineData("{ type: 'click', button: 0, ctrlKey: true, shiftKey: true, altKey: true }", false)]
	[InlineData("{ type: 'click', button: 2, ctrlKey: true, shiftKey: true }", false)]
	[InlineData("{ type: 'auxclick', button: 1, ctrlKey: true, shiftKey: true }", false)]
	public void OnlyACtrlShiftLeftClick_ForcesTheSystemBrowser(string ev, bool expected) => Assert.Equal(expected, Eval($"g.forcesBrowser({ev})").GetBoolean());
}
