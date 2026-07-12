# ClaudeMaximus — Build Notes

## Building the project

Build normally without the `-o` flag:
```
dotnet build code/ClaudeMaximus.sln
```

The build output goes to `code/ClaudeMaximus/bin/Debug/net9.0-windows10.0.17763.0/` (platform-specific TFM).

The app runs from a separate directory (typically `code/ClaudeMaximus/publish/`), NOT from the build output. Therefore the build output directory should NOT be locked during builds. If you encounter a file-lock error on the build output, investigate — it likely means the app was accidentally launched from the build output.

Do NOT use `-o` to redirect build output. The self-update mechanism scans `bin/Debug/net*/` for the newest build and auto-copies to `publish/` on each app startup.

Do NOT use the `Tempcmx-build` folder — it is deprecated.
