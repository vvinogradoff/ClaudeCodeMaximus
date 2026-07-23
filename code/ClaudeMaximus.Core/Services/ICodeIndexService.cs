using System.Collections.Generic;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public interface ICodeIndexService
{
	Task GetOrCreateIndexAsync(string workingDirectory);
	void ReleaseIndex(string workingDirectory);
	IReadOnlyList<IndexedFileModel> SearchFiles(string workingDirectory, string query, int maxResults = 15);
	IReadOnlyList<CodeSymbolModel> SearchSymbols(string workingDirectory, string query, int maxResults = 15);
	bool IsIndexReady(string workingDirectory);
}
