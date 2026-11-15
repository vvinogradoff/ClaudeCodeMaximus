using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Rendering;

/// <summary>
/// Renders untrusted transcript text to HTML: raw HTML is disabled (it is escaped), generic attributes are not enabled,
/// images become plain text and only http/https links survive (others become inert text). Together with the page's
/// Content-Security-Policy this keeps transcript text from running anything.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MarkdigEntryRenderer : IEntryRenderer, IMarkdownRenderer
{
	private const string BlockedLinkClass = "blocked";
	private const string BlockedLinkTarget = "#";
	private const string ImagePlaceholder = "[image]";
	private const string AssistantRole = "assistant";

	private readonly MarkdownPipeline _pipeline;
	private readonly UrlPolicy _urlPolicy;
	private readonly MediaLinkDetector? _media;
	private readonly LocalPathDetector? _paths;

	public MarkdigEntryRenderer(UrlPolicy urlPolicy, MediaLinkDetector? media = null, LocalPathDetector? paths = null)
	{
		_urlPolicy = urlPolicy;
		_media = media;
		_paths = paths;
		_pipeline = new MarkdownPipelineBuilder()
			.DisableHtml()
			.UsePipeTables()
			.UseEmphasisExtras()
			.UseTaskLists()
			.UseAutoLinks()
			.Build();
	}

	public TranscriptEntryView Render(SessionEntryDto entry)
	{
		var truncated = entry.Truncated == true;
		var assistant = entry.Role == AssistantRole;
		return new TranscriptEntryView(
			entry.Index,
			RoleCode(entry.Role),
			HeaderText(entry),
			RenderMarkdown(entry.Content),
			truncated,
			entry.FullLength ?? entry.Content.Length,
			EstimateHeight(entry.Content),
			assistant ? _media?.Detect(entry.Content) : null,
			assistant ? _paths?.Detect(entry.Content) : null);
	}

	/// <summary>Markdown to sanitized HTML.</summary>
	public string RenderMarkdown(string markdown)
	{
		var document = Markdown.Parse(markdown, _pipeline);
		Sanitize(document);
		using var writer = new StringWriter();
		var renderer = new HtmlRenderer(writer);
		_pipeline.Setup(renderer);
		renderer.Render(document);
		writer.Flush();
		return writer.ToString();
	}

	private void Sanitize(MarkdownDocument document)
	{
		foreach (var link in document.Descendants<LinkInline>().ToList())
		{
			if (link.IsImage)
			{
				link.ReplaceBy(new LiteralInline(ImagePlaceholder));
			}
			else if (!_urlPolicy.IsAllowed(link.Url))
			{
				link.Url = BlockedLinkTarget;
				link.GetAttributes().AddClass(BlockedLinkClass);
			}
		}

		foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
		{
			if (!_urlPolicy.IsAllowed(autolink.Url))
				autolink.ReplaceBy(new LiteralInline(autolink.Url));
		}
	}

	private static string RoleCode(string role) => role switch
	{
		"user" => "U",
		"assistant" => "A",
		"compaction" => "C",
		_ => "S",
	};

	private static string HeaderText(SessionEntryDto entry)
	{
		var text = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
		foreach (var part in new[] { entry.ModelId, entry.Effort, entry.ProfileName })
		{
			if (!string.IsNullOrWhiteSpace(part))
				text += " · " + part;
		}

		return text;
	}

	private static int EstimateHeight(string content)
	{
		const double Base = 34;
		const double CodeLine = 17;
		const double TextLine = 20;
		const double BlankLine = 8;
		const double FenceGap = 6;
		const double CharsPerLine = 95.0;

		var height = Base;
		var inCode = false;
		foreach (var line in content.Split('\n'))
		{
			if (line.StartsWith("```", StringComparison.Ordinal))
			{
				inCode = !inCode;
				height += FenceGap;
			}
			else if (line.Length == 0)
			{
				height += BlankLine;
			}
			else
			{
				height += inCode ? CodeLine : TextLine * Math.Max(1, Math.Ceiling(line.Length / CharsPerLine));
			}
		}

		return (int)height;
	}
}
