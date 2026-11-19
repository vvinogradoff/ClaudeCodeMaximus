# The daemon elevated while WPF runs normally — as built

> Draft for Architect review, 2026-11-19. Author: Developer/Claude.
> Requirements: FR.32.10–FR.32.14 (docs approved as `dea39b6`), and the fix of FR.31.2 (paste of files). Use case: UC.HS.08. Test cases: TC.HS.23–TC.HS.30. Protocol: tdcp-spec 10.A10.
> Built on branch `wpf/elevated-daemon` and merged to master as `4b979ef`. The paste fix is `8680574`.

## 1. What it is

When WPF itself ran as administrator, Windows refused files dragged from Explorer into the prompt input, and every session ran elevated whether it needed to or not. Some work does need administrator rights, above all the WPF end-to-end runner `tools\e2e\Run-WpfE2E.ps1` (NR.6). So **WPF always runs normally**, and the **daemon host** runs elevated only when the User chooses so. Everything the host starts (each session's `claude` process and its tools) runs elevated with it.

What the user sees:

- **Settings → Startup** (FR.32.10–FR.32.12):
  - The checkbox **Start daemon elevated**, below *Start the background daemon when none is running*. Default off; saved with Save, and the close guard (FR.4.7) knows it.
  - A status line: "The daemon runs elevated (as administrator)." or "The daemon runs normally." Nothing while no daemon is connected.
  - Two buttons, **Restart daemon elevated** and **Restart daemon normal**. They act at once and are not part of Save.
- **The administrator prompt** appears when WPF starts the host with the checkbox on, and on Restart daemon elevated.
- **A banner** "The daemon runs normally, but Start daemon elevated is on." with *Restart daemon elevated*, when the checkbox is on and the daemon runs normally (FR.32.12). Nothing restarts by itself.
- **A declined or failed prompt** starts the daemon normally, and the banner says "The daemon runs normally: the administrator prompt was declined." (or names the failure). The prompt does not come back by itself (FR.32.13).
- **The stale banner's Restart daemon** brings the daemon back the way it ran: an elevated one comes back elevated (FR.32.11).
- **Pasting files** copied in Explorer into the prompt input works (FR.31.2). It never worked before, elevated or not.
- **A banner when WPF itself runs as administrator** (FR.32.15, added after first use, merge `60286f4`): "This window runs as administrator. Files can't be dropped from Explorer, and every daemon it starts runs elevated." It has no button; the fix is to start WPF normally. The start log says which way WPF runs.

**What elevated means.** While the daemon runs elevated, every turn runs as administrator: typed, scheduled, worker, Telegram and Tessyn Web turns. Through Tessyn Web, whoever can sign in can then have a session run administrator commands on the PC.

## 2. Key decisions and why

### The Architect's answers of 2026-11-19

| Question | Answer | Why it matters |
|---|---|---|
| How do sessions get administrator rights once Tessyn runs normally (A1) | WPF runs normally. The checkbox *Start daemon elevated* (default off) and the buttons *Restart daemon elevated* and *Restart daemon normal* | The default stays non-elevated. The User elevates when needed and the daemon keeps that way until it ends |
| The drafted requirements (A3) | Approved as drafted | They fix the calls below |
| The checkbox is on but a normal daemon is found | A banner offers Restart daemon elevated; nothing restarts by itself | A restart ends running sessions; only the User may decide that |
| The prompt is declined | The daemon starts normally with a notice; no new prompt until WPF's next start or Restart daemon elevated | Tessyn stays usable, and the once-per-60 s start rule cannot repeat the prompt every minute |

### Design calls

- **The whole host is elevated, never a single session.** Windows elevates a program only through its administrator prompt (a shell start with the `runas` verb). A program started that way cannot have its input and output piped to a normal process. Every turn comes from `claude`'s stdout, so a normal daemon cannot start one `claude` elevated. That would need an elevated broker that relays the pipes, which was left out (A1).
- **The host is a shell start with `runas`.** The host needs no pipes and has no window (`--background`). A shell start takes the arguments as one command line, so they are joined and quoted (`ProcessHostStarter.CreateStartInfo`) instead of `ArgumentList`.
- **The new buttons do not restart WPF.** The client reconnects to the same endpoint and token by itself and follows the new run (FR.32.6). On the run change, `ConnectionViewModel` reads the new pid, the new `elevated` value and the build again.
- **The stale banner's restart starts the new host itself, then restarts WPF.** That is how it keeps the daemon's way. The restarted client finds the running host and attaches to it.
- **The daemon reports its own elevation** in `client.hello` (`Environment.IsPrivilegedProcess`, read once at start). A normal client cannot always query an elevated process's token.
- **The refusal is remembered per WPF run.** After a declined or failed prompt, automatic starts go normally; only Restart daemon elevated asks again. An approved prompt clears it.
- **A shell start without a process handle counts as started.** `Process.Start` may return no process for a shell start that did start the host. Starting a normal host beside it could take the guard first and leave the User with a normal daemon after approving.

## 3. How it works

### The Windows rules behind it

These rules decide the design. A later change that breaks one of them breaks the restart from a normal WPF without any error in the tests.

- **Messages and drops (UIPI).** A normal process may not send most window messages, or drop files, into an elevated one. Explorer runs normally, so an elevated WPF gets no drops. The shell's tray messages to an elevated host pass the same filter, so `TrayMessageFilter` lets two of them through (the NotifyIcon callback and `TaskbarCreated`).
- **Named objects.** An object created by an elevated process gets a High mandatory label with no-write-up, and a normal process may then not set it. The host's exit event is therefore created with its own descriptor (`UserSignalledEvent`):
  - the user may wait on it and set it (SYNCHRONIZE | EVENT_MODIFY_STATE);
  - SYSTEM and Administrators have full access;
  - the label is Medium with no-write-up.
  The guard mutex needs nothing: opening it from a normal process is refused, and that already counts as "a host runs" (`DaemonHostGuard`, `MutexHostPresence`).
- **Reading the process.** `Process.MainModule` reads the process's memory, which an elevated process refuses to a normal one. The host's folder is read with `QueryFullProcessImageName` and the limited query right instead (`ProcessImagePath`). Waiting for the process to end and asking whether it has ended both work. Terminating it does not: an exit always goes through the exit event.
- **Files.** Files the elevated daemon writes under `%AppData%\TessynDesktop` and in the profile folders keep the folder's inherited permissions, so a later normal daemon can use them. No special permissions are set.
- **Paste (FR.31.2).** A plain `TextBox` turns Paste off when the clipboard holds no text, and Explorer's Copy of files holds none. `DataObject.Pasting` is therefore never raised for copied files. The prompt input takes over the Paste command itself (PreviewCanExecute and PreviewExecuted) when the clipboard holds a file list without text.

### The flows

- **Automatic start** (the connect loop, no daemon found): `EnsureStartedAsync`. The way is chosen inside the try that clears the starting flag: elevated when the setting is on and no refusal happened in this run. Then the newest build is copied into `host\` and the host is started. An elevated start that is declined or fails raises `ElevationRefused` and starts the host normally.
- **Restart daemon elevated / normal**:
  1. `ConnectionViewModel.SwitchDaemonAsync` calls `DaemonRestarter.SwitchAsync`.
  2. The restarter asks first, sets the exit event, waits for the process to end, then calls `IDaemonHostLauncher.StartNowAsync(mode)`. That ignores the start setting and the cool-down, never starts beside a host that holds the guard, and holds the starting flag, so the connect loop starts nothing meanwhile.
  3. On `Restarted` the status becomes unknown. The client reconnects, and `Resynced` with `run-changed` runs `FollowNewRunAsync` (pid, `elevated`, build check).
- **The stale banner's Restart daemon**: `RestartAsync(..., keep)` with the daemon's current way. The restarter starts the new host, then restarts WPF.
- **Status and banner.**
  - `DaemonElevated` is read from the hello at connect, again on Connected after a reconnect, and on a run change. It is null while disconnected.
  - The banner shows while connected, with the daemon normal, and either a refusal notice or the setting on.
  - A change of the `daemonHost` settings section re-evaluates it.

## 4. Code map

| File | Role |
|---|---|
| `code/TessynDesktop.WPF/Switch/DaemonHostLauncher.cs` | `IDaemonHostLauncher` with `StartNowAsync` and `ElevationRefused`; `ProcessHostStarter.CreateStartInfo` (the `runas` start); the refusal memory |
| `code/TessynDesktop.WPF/Switch/DaemonRestarter.cs` | `SwitchAsync`, `RestartAsync(keep)`, `EndDaemonAsync`; `ProcessDaemonBuildProbe` reads the folder through `ProcessImagePath` |
| `code/TessynDesktop.WPF/Switch/HostStartMode.cs`, `ElevationRefusal.cs`, `IDaemonModeControl.cs`, `ProcessImagePath.cs` | The way of a start; a refusal and its notice; what the Startup tab sees of the connection; the limited-right image path |
| `code/TessynDesktop.WPF/ViewModels/ConnectionViewModel.cs` | Implements `IDaemonModeControl`: `DaemonElevated`, `ElevationNote`, the banner, the two commands, following a new run |
| `code/TessynDesktop.WPF/ViewModels/Settings/StartupSectionViewModel.cs`, `Views/Settings/StartupSettingsView.xaml` | The checkbox, the status line, the buttons (a weak `PropertyChangedEventManager` subscription to the connection) |
| `code/TessynDesktop.WPF/Views/MainWindow.xaml` | The elevation banner, below the stale banner |
| `code/TessynDesktop.WPF/State/DaemonHostSettings.cs` | `StartDaemonElevated` in the `daemonHost` section |
| `code/TessynDesktop.WPF/Constants.Switch.cs`, `Constants.Settings.cs`, `Constants.Shell.cs` | Texts, the `runas` verb, `ERROR_CANCELLED` (1223), the default |
| `code/TessynDesktop.WPF/ServiceRegistration.cs` | `IsHostStartElevated`; the launcher into the restarter; the settings into the connection; the connection into Settings |
| `code/TessynDesktop.WPF/Views/SessionPane.xaml.cs`, `Services/FileDropReader.cs` | The Paste command for files (FR.31.2) |
| `code/TessynDesktop.Protocol/Models/Remote/HelloResult.cs`, `code/TessynDesktop.Core/Services/Daemon/ClientRpcModule.cs` | `elevated` in `client.hello` |
| `code/TessynDesktop.Core/Services/Daemon/UserSignalledEvent.cs` (+ `.Native.cs`), `DaemonHostExit.cs` | The exit event with its descriptor |
| `code/TessynDesktop/Services/TrayMessageFilter.cs`, `HostTray.cs` | The tray messages through UIPI on an elevated host |

## 5. Testing

**Unit tests.**
- `DaemonElevationTests` (WPF): TC.HS.23–TC.HS.28, and the image path of TC.HS.29.
- `UserSignalledEventTests` and `HelloTests` (Core): the exit event's descriptor, read back from the real event (TC.HS.29), and `elevated`.
- `ComposerViewTests` and `ComposerAttachmentTests`: the paste fix. The old paste test raised a `DataObject.Pasting` event that WPF never raises for a TextBox; the new one runs the real Paste command through a clipboard seam.
- `DaemonHostLauncherTests`, `DaemonRestarterTests` and `SideBySideTests` only follow the new starter signature.

**Suites at the merge:** Core 3983, WPF 2299, Web 146, WPF E2E 130 (through `Run-WpfE2E.ps1` as `tessyn-e2e`), Web E2E 24 and 1 skipped.

**Not automated.**
- **The real administrator prompt.** It is on the secure desktop and cannot be scripted: TC.HS.30 is a manual list (Focus A4 at the time of writing).
- **Elevated starts.** They are tested with a fake starter. The real `ProcessHostStarter` is checked through `CreateStartInfo` only.
- **Access across elevation.** It is not exercised: the tests check the descriptor instead.
- **The restart buttons in E2E.** Host start is off in test environments (FR.32.3), so no E2E test covers them.
- **The tray filter.** It has no test; the host project has no test project.

## 6. Gotchas for later work

- **Keep the exit event's descriptor.** With default security, a normal WPF cannot stop an elevated host, and Restart says "this daemon cannot be restarted from here". Any new named object a normal client must reach (an event, a mutex, a pipe) needs the same treatment.
- **Daemons started before `4b979ef`** have the old exit event. The first switch-over to a normal WPF needs one manual exit of the elevated daemon (Focus A4).
- **Never read an elevated host's memory** (`Process.MainModule`, module lists) and never kill it from WPF. Use `ProcessImagePath`, and end it through the exit event.
- **The connect loop ends after its first connect.** After an in-place restart nothing else starts a host, so the restarter must (`StartNowAsync`). The starting flag keeps a late connect loop from starting a second host.
- **The refusal memory must stay.** The connect loop retries every few seconds with a 60 s cool-down; without it a declined prompt would come back every minute.
- **WPF must never run elevated.** It would block drag-and-drop again, and WebView2 ignores its DevTools port in an elevated process (an NR.6 gotcha). On 2026-11-19 it ran elevated unnoticed: a taskbar pin had *Run as administrator* set, and WPF's own restarts (self-update, Restart daemon) keep the elevation of the process that starts them. That is why FR.32.15 shows a banner (`ConnectionViewModel.IsClientElevated`).
- **The tray filter depends on WinForms.** It assumes the NotifyIcon callback message is `WM_USER + 1024`, as in WinForms today. If the tray of an elevated host stops answering, check that first (TC.HS.30 has the tray check).
- **The working directory may be ignored.** Windows may ignore it for an elevated start. Today only Claude Assist calls use the host's working directory (`ClaudeProcessManager.RunPrintModeAsync`, with no tools and no session files), so it does not matter. New code in the host should use `AppContext.BaseDirectory`, not the current directory.
- **Status updates come from the UI thread.** The Startup tab subscribes weakly (`PropertyChangedEventManager`), so a closed Settings window is not kept alive. The connection raises its changes through the UI dispatcher; keep it that way.
- **The paste gotcha.** For any other `TextBox` that should accept pasted files, hook the Paste command; `DataObject.Pasting` never comes without text on the clipboard.
