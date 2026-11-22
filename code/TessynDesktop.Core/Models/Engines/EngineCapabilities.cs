namespace TessynDesktop.Models.Engines;

/// <summary>What a session engine supports (FR.33.1). Clients hide the features an engine lacks instead of faking them.</summary>
/// <param name="PersistentProcess">A session can keep one process open across turns (Streaming Session Mode, FR.19).</param>
/// <param name="OneShotProcess">A turn can run in a process of its own that exits when the turn ends.</param>
/// <param name="InBandInterrupt">A running turn can be interrupted without killing its process.</param>
/// <param name="ReportsCost">A turn reports its cost in USD (FR.17).</param>
/// <param name="NativeCompaction">The engine compacts its own conversation; without it Tessyn rewrites the transcript (FR.11.6).</param>
/// <param name="Chrome">The <c>--chrome</c> toggle applies.</param>
/// <param name="LocalModels">Local Ollama models run through this engine (FR.12.14), with the Local Tool Profile (FR.12.21).</param>
/// <param name="MidTurnCorrection">A correction can be sent while a turn runs (<see cref="Services.ISessionEngine.SendCorrection"/>).</param>
/// <remarks>Created by Claude</remarks>
public sealed record EngineCapabilities(
	bool PersistentProcess,
	bool OneShotProcess,
	bool InBandInterrupt,
	bool ReportsCost,
	bool NativeCompaction,
	bool Chrome,
	bool LocalModels,
	bool MidTurnCorrection);
