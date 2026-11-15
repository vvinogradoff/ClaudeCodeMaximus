# Shell Command Notes

## dotnet — chain commands
`&&` does NOT work in Windows cmd, but works fine in bash (which Claude Code uses here). Use `&&` normally.

## dotnet build — standard build

Build normally without the `-o` flag:
```
dotnet build code/TessynDesktop.sln
```

The app runs from `C:\Projects\Personal\Tessyn\[Publish]`, not from the build output. Therefore the build output directory (`bin\Debug\net9.0-windows10.0.17763.0\`) should not be locked during builds.

If the app is accidentally launched directly from `bin\Debug\net9.0\`, a warning icon (⚠) appears in the title bar and self-update is disabled for that session.

Do NOT use the `Tempcmx-build` folder — it is deprecated. Do NOT use `-o` to redirect build output — the self-update mechanism looks for builds in `bin/Debug/net9.0/`.

## Avalonia — compiled bindings in DataTemplates
`x:DataType` on a `DataTemplate` or `TreeDataTemplate` inside a control's `DataTemplates` collection does NOT correctly scope compiled bindings — Avalonia resolves `{Binding X}` against the outer control's `x:DataType` instead of the template's type, returning null silently.

**Fix:** add `x:CompileBindings="False"` to each such template. This overrides the project-level `AvaloniaUseCompiledBindingsByDefault=true` for that scope and uses runtime reflection binding, which correctly uses the item's actual runtime type.

## git add — CRLF files with core.autocrlf=input

When `core.autocrlf=input` is set, `git add` refuses to stage files with CRLF line endings (error: "CRLF would be replaced by LF"). New files written by the Write tool on Windows have CRLF.

**Fix:** use `git -c core.safecrlf=false add <file>` to stage without the safety check.

**DO NOT** try to convert line endings with PowerShell via bash — backtick escaping in `-replace` patterns gets mangled by bash, corrupting file content.

## Editing source files with scripts (Git Bash and PowerShell)
Large heredocs (`cat > file <<'EOF'`) and `python - <<'PY'` blocks that contain nested quotes, `'''` or C# raw string literals (`"""`) fail with "unexpected EOF while looking for matching quote", and nothing is executed.

**Fix:** write the script or the file with the Write tool (a `.py` file in `%TEMP%`, run with `python <path>`), then run it. Python's text mode reads CRLF files as LF and writes LF back; git normalizes line endings (`core.autocrlf=true` here), so this causes no diff churn. Match tab counts exactly when replacing code (a missed tab fails the `assert old in s` check, which is a good thing).
