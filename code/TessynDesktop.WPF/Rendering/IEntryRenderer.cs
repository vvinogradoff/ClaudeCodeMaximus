using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Rendering;

/// <summary>Turns a transcript entry into the HTML the transcript page shows. Pure and thread-safe.</summary>
/// <remarks>Created by Claude</remarks>
public interface IEntryRenderer
{
	TranscriptEntryView Render(SessionEntryDto entry);
}
