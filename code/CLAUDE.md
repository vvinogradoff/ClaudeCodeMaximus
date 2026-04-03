# TessynDesktop — Build Notes

## Building the project

Build normally without the `-o` flag:
```
dotnet build code/TessynDesktop.sln
```

The build output goes to `code/TessynDesktop/bin/Debug/net9.0/`.

The app runs from a separate directory (typically `code/TessynDesktop/publish/`), NOT from `bin/Debug/net9.0/`. Therefore `bin/Debug/net9.0/` should NOT be locked during builds. If you encounter a file-lock error on `bin/Debug/net9.0`, investigate — it likely means the app was accidentally launched from the build output.

Do NOT use `-o` to redirect build output. The self-update mechanism looks for the newest build in `bin/Debug/net9.0/` under the configured source codes location.

Do NOT use the `Tempcmx-build` folder — it is deprecated.
