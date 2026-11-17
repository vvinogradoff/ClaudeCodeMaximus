#:project ../code/TessynDesktop.Core/TessynDesktop.Core.csproj
#:property TargetFramework=net10.0-windows10.0.17763.0
#:property PublishAot=false

// Removes the duplicate USER/ASSISTANT entries the JSONL write-back left in session .txt files (fix/duplicate-assistant).
// The rule lives in SessionDuplicateCleaner (TessynDesktop.Core): an entry goes only when the previous entry has the same role, the same
// MULTI-LINE content, was written at most 10 s earlier, and the removed copy carries no header metadata. Single-line repeats are kept.
//
//   dotnet run tools/CleanDuplicateEntries.cs -- [--root <sessions folder>]                       DRY RUN (the default; writes nothing)
//   dotnet run tools/CleanDuplicateEntries.cs -- [--root <dir>] --apply --backup-dir <dir>        rewrite; originals are copied first
//
// --root defaults to %AppData%\TessynDesktop\sessions. Close the Tessyn apps before --apply: a running daemon appends to these files.

using System.Text;
using TessynDesktop.Services;

var root      = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TessynDesktop", "sessions");
string? backupDir = null;
var apply     = false;

for (var i = 0; i < args.Length; i++)
{
	switch (args[i])
	{
		case "--root" when i + 1 < args.Length:       root = args[++i]; break;
		case "--backup-dir" when i + 1 < args.Length: backupDir = args[++i]; break;
		case "--apply":                               apply = true; break;
		default:
			Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
			return 2;
	}
}

if (apply)
{
	if (string.IsNullOrWhiteSpace(backupDir))
	{
		Console.Error.WriteLine("--apply requires --backup-dir.");
		return 2;
	}

	var rootFull   = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
	var backupFull = Path.GetFullPath(backupDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
	if (backupFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
	{
		Console.Error.WriteLine("--backup-dir must not be inside --root.");
		return 2;
	}

	Directory.CreateDirectory(backupDir);
}

Console.WriteLine($"Mode: {(apply ? "APPLY" : "DRY RUN (nothing is written)")}   Root: {root}");

int files = 0, changed = 0, removedTotal = 0;
foreach (var path in Directory.EnumerateFiles(root, "*.txt"))
{
	files++;
	var raw    = File.ReadAllText(path, Encoding.UTF8);
	var result = SessionDuplicateCleaner.Clean(raw);
	if (result.Removed.Count == 0)
		continue;

	changed++;
	removedTotal += result.Removed.Count;
	Console.WriteLine($"{Path.GetFileName(path)}: {result.Removed.Count} duplicate(s)");
	foreach (var r in result.Removed)
		Console.WriteLine($"    {r.Timestamp:u} {r.Role} {r.Length} chars  {r.Excerpt}");

	if (apply)
	{
		File.Copy(path, Path.Combine(backupDir!, Path.GetFileName(path)), overwrite: true);
		// The encoding the daemon writes (UTF-8 with a BOM); the content is otherwise exactly what was read.
		File.WriteAllText(path, result.CleanedText, new UTF8Encoding(true));
	}
}

Console.WriteLine($"Scanned {files} file(s); {changed} file(s) {(apply ? "cleaned," : "would lose")} {removedTotal} duplicate entr(ies).");
return 0;
