using System.Collections.Generic;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public interface ICodeIndexService
{
	/// <summary>Returns true when the caller now holds a reference that it must give back with <see cref="ReleaseIndex"/>; false when nothing was acquired (empty path, failed or abandoned build).</summary>
	Task<bool> GetOrCreateIndexAsync(string workingDirectory);
	void ReleaseIndex(string workingDirectory);
	IReadOnlyList<IndexedFileModel> SearchFiles(string workingDirectory, string query, int maxResults = 15);
	IReadOnlyList<CodeSymbolModel> SearchSymbols(string workingDirectory, string query, int maxResults = 15);
	bool IsIndexReady(string workingDirectory);
}
