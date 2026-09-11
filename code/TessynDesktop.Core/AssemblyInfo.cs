using System.Runtime.CompilerServices;

// Lets TessynDesktop.Tests assert directly on a small number of internal seams (e.g.
// ClaudeProcessManager.BuildArguments) instead of duplicating CLI-arg-assembly logic in a test
// double, without widening those members to public API.
[assembly: InternalsVisibleTo("TessynDesktop.Tests")]
