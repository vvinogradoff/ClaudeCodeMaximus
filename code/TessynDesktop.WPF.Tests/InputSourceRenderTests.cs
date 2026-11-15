using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>The entry source reaches the page: a scheduled or supervisor input is rendered with its source, the user's own is not.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class InputSourceRenderTests
{
	private readonly MarkdigEntryRenderer _renderer = new(new UrlPolicy());

	private TranscriptEntryView Render(string role, string? source) =>
		_renderer.Render(new SessionEntryDto { Index = 3, Timestamp = DateTimeOffset.UtcNow, Role = role, Content = "text", Source = source });

	[Theory]
	[InlineData("scheduled")]
	[InlineData("orchestrated")]
	[InlineData("telegram")]
	[InlineData("web")]
	public void A_user_entry_keeps_its_source(string source)
	{
		var view = Render("user", source);

		Assert.Equal("U", view.Role);
		Assert.Equal(source, view.Source);
	}

	[Fact]
	public void The_desktop_users_entry_has_no_source()
	{
		Assert.Null(Render("user", null).Source);
	}
}
