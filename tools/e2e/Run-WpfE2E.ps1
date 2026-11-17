<#
.SYNOPSIS
  Runs the WPF E2E tests (the only supported way): one run at a time machine-wide, in a kill-on-close job object, with output
  streamed and the exit code returned.

.DESCRIPTION
  * Serialises: a machine-wide named mutex lets only ONE WPF E2E run execute at a time, however many workers start one. Parallel
    runs were the main reason the user's foreground, keyboard focus and taskbar were disturbed. Later runs wait their turn.
  * Cleans up: the test process tree lives in a job object with KILL_ON_JOB_CLOSE, so every app / WebView2 / testhost process the
    run started dies when the run ends, times out, or this script is killed.
  * Tells the test project it was started by the runner (TESSYN_E2E_RUNNER=1); the project fails fast otherwise.
  * -Hidden (EXPERIMENTAL): additionally runs the tree on a private desktop (CreateDesktop + STARTUPINFO.lpDesktop). Nothing is
    drawn or focused on the user's screen, UI Automation and popups work there, but WebView2 does NOT: its browser process is
    started on the interactive desktop and the controller fails with "Invalid window handle", so every WebView2-dependent E2E
    test fails. Use it only for probes and WebView2-free tests.

  * Test-user mode (DEFAULT when the credential exists; -AsTestUser forces it, -AsCurrentUser or -Hidden opt out): the whole
    `dotnet test` tree runs as the local user `tessyn-e2e` from a scheduled task with a password logon. That is a batch logon
    in session 0 with its own window station (Service-0x...$), so the app, WebView2's browser process and UI Automation live
    there and nothing touches the Architect's desktop, focus or taskbar. Needs: an elevated caller (registering a password task
    for another user), the DPAPI credential at %AppData%\TessynDesktop\e2e\tessyn-e2e.cred.xml, and the "Log on as a batch
    job" right on the account. Output is tailed from a log file, the exit code comes from a file, and the task, the run folder
    (<repo>\.e2e-run) and every leftover process of the account are removed in `finally`. The password is never printed.
    Without the credential file the runner WARNS and falls back to the plain interactive run.

  Everything not consumed by the script's own parameters is passed to `dotnet test`. Without a project argument the WPF E2E
  project is used.

.EXAMPLE
  tools\e2e\Run-WpfE2E.ps1 --no-build
  tools\e2e\Run-WpfE2E.ps1 --no-build --filter "FullyQualifiedName~Recent"
  tools\e2e\Run-WpfE2E.ps1 -Hidden -Command powershell.exe -CommandArguments '-File','probe.ps1'
#>
[CmdletBinding(PositionalBinding = $false)]
param(
	[string]$Command = 'dotnet',
	[string[]]$CommandArguments,
	[int]$TimeoutMinutes = 60,
	[int]$WaitMinutes = 120,
	[switch]$Hidden,
	[switch]$AsTestUser,
	[switch]$AsCurrentUser,
	[Parameter(ValueFromRemainingArguments = $true)][string[]]$TestArguments
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

$defaultTestCommand = (-not $CommandArguments) -and $Command -eq 'dotnet'
$testProject = $null
if (-not $CommandArguments) {
	$CommandArguments = @('test')
	if (-not ($TestArguments | Where-Object { $_ -match '\.(csproj|sln)$' -or $_ -match 'TessynDesktop|Tessyn\.Web' })) {
		$CommandArguments += (Join-Path $repo 'code\TessynDesktop.WPF.E2E.Tests')
	}
	$CommandArguments += $TestArguments
}

Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public sealed class HiddenDesktopRun : IDisposable
{
	const uint GENERIC_ALL = 0x10000000, CREATE_SUSPENDED = 0x4, CREATE_NO_WINDOW = 0x08000000, CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
	const int STARTF_USESTDHANDLES = 0x100, STARTF_USESHOWWINDOW = 0x1, SW_HIDE = 0, JobObjectExtendedLimitInformation = 9;
	const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct STARTUPINFO { public int cb; public string lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
	[StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
	[StructLayout(LayoutKind.Sequential)] struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }
	[StructLayout(LayoutKind.Sequential)] struct JOBOBJECT_BASIC_LIMIT_INFORMATION { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
	[StructLayout(LayoutKind.Sequential)] struct IO_COUNTERS { public ulong a, b, c, d, e, f; }
	[StructLayout(LayoutKind.Sequential)] struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION { public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic; public IO_COUNTERS Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);
	[DllImport("user32.dll", SetLastError = true)] static extern bool CloseDesktop(IntPtr desktop);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr sa, string name);
	[DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int cls, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int size);
	[DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
	[DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateJobObject(IntPtr job, uint code);
	[DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
	[DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);
	[DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
	[DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFile(string name, uint access, uint share, ref SECURITY_ATTRIBUTES sa, uint disposition, uint flags, IntPtr template);

	IntPtr _desktop, _job, _log, _process;
	public string DesktopName { get; private set; }
	public int ProcessId { get; private set; }

	/// <summary>desktopName empty: the run stays on the interactive desktop (job object only).</summary>
	public HiddenDesktopRun(string desktopName)
	{
		DesktopName = desktopName;
		if (!string.IsNullOrEmpty(desktopName))
		{
			_desktop = CreateDesktop(desktopName, IntPtr.Zero, IntPtr.Zero, 0, GENERIC_ALL, IntPtr.Zero);
			if (_desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDesktop failed");
		}
		_job = CreateJobObject(IntPtr.Zero, null);
		var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
		info.Basic.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
		if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(info))) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed");
	}

	/// <summary>Starts the command line with stdout+stderr going to logPath. Returns immediately.</summary>
	public void Start(string commandLine, string workingDirectory, string logPath)
	{
		var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)), bInheritHandle = 1 };
		_log = CreateFile(logPath, 0x40000000 | 0x80000000, 3, ref sa, 2, 0x80, IntPtr.Zero);   // read+write, share r/w, CREATE_ALWAYS
		if (_log == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "cannot create log file");
		var si = new STARTUPINFO();
		si.cb = Marshal.SizeOf(si);
		if (!string.IsNullOrEmpty(DesktopName)) si.lpDesktop = "WinSta0\\" + DesktopName;
		si.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
		si.wShowWindow = SW_HIDE;
		si.hStdOutput = _log; si.hStdError = _log; si.hStdInput = IntPtr.Zero;
		PROCESS_INFORMATION pi;
		var cmd = new StringBuilder(commandLine);
		if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, true, CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_BREAKAWAY_FROM_JOB, IntPtr.Zero, workingDirectory, ref si, out pi))
		{
			// The caller may sit in a job that forbids breakaway; retry without it (nested jobs are allowed on Windows 8+).
			if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, true, CREATE_SUSPENDED | CREATE_NO_WINDOW, IntPtr.Zero, workingDirectory, ref si, out pi))
				throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed");
		}
		if (!AssignProcessToJobObject(_job, pi.hProcess)) throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed");
		ResumeThread(pi.hThread);
		CloseHandle(pi.hThread);
		_process = pi.hProcess;
		ProcessId = pi.dwProcessId;
	}

	/// <summary>Waits up to ms; true when the process ended.</summary>
	public bool Wait(uint ms) { return WaitForSingleObject(_process, ms) == 0; }
	public int ExitCode { get { uint c; GetExitCodeProcess(_process, out c); return unchecked((int)c); } }

	public void Dispose()
	{
		if (_job != IntPtr.Zero) { TerminateJobObject(_job, 1); CloseHandle(_job); _job = IntPtr.Zero; }  // kills the whole process tree
		if (_process != IntPtr.Zero) { WaitForSingleObject(_process, 5000); CloseHandle(_process); _process = IntPtr.Zero; }
		if (_log != IntPtr.Zero && _log != new IntPtr(-1)) { CloseHandle(_log); _log = IntPtr.Zero; }
		if (_desktop != IntPtr.Zero)
		{
			// A desktop cannot be closed while a thread still uses it; give the killed tree a moment to go away.
			for (var i = 0; i < 20 && !CloseDesktop(_desktop); i++) System.Threading.Thread.Sleep(250);
			_desktop = IntPtr.Zero;
		}
	}
}
'@

function Quote([string]$a) {
	if ($a -match '[\s"]' -or $a -eq '') { '"' + (($a -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"' } else { $a }
}

$credPath = Join-Path $env:AppData 'TessynDesktop\e2e\tessyn-e2e.cred.xml'
$useTestUser = $false
if (-not $AsCurrentUser -and -not $Hidden) {
	if (Test-Path $credPath) {
		if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
			Write-Host '[Run-WpfE2E] ERROR: test-user mode needs an elevated caller (registering a password task for tessyn-e2e). Run elevated, or pass -AsCurrentUser to accept desktop disturbance.'
			exit 126
		}
		$useTestUser = $true
	}
	elseif ($AsTestUser) { Write-Host "[Run-WpfE2E] ERROR: -AsTestUser but the credential file is missing: $credPath"; exit 126 }
	else { Write-Host "[Run-WpfE2E] WARNING: credential file $credPath not found; running as the current user on the interactive desktop (may disturb focus)." }
}

function Remove-StaleTestUserRuns {
	Get-ScheduledTask -TaskName 'TessynE2E-*' -ErrorAction SilentlyContinue | ForEach-Object {
		Stop-ScheduledTask -InputObject $_ -ErrorAction SilentlyContinue
		Unregister-ScheduledTask -InputObject $_ -Confirm:$false -ErrorAction SilentlyContinue
	}
	# Whatever the stopped task left behind (the task job normally kills the tree; this is the guarantee).
	for ($i = 0; $i -lt 10; $i++) {
		$left = @(Get-Process -IncludeUserName -ErrorAction SilentlyContinue | Where-Object { $_.UserName -like '*\tessyn-e2e' })
		if ($left.Count -eq 0) { break }
		$left | Stop-Process -Force -ErrorAction SilentlyContinue
		Start-Sleep -Milliseconds 300
	}
}

$name = if ($Hidden) { "TessynE2E-$PID" } else { '' }
$log = Join-Path ([IO.Path]::GetTempPath()) "tessyn-e2e-$PID.log"
$exe = (Get-Command $Command).Source
$commandLine = (@((Quote $exe)) + ($CommandArguments | ForEach-Object { Quote $_ })) -join ' '

# One WPF E2E run at a time on this machine.
$gate = New-Object System.Threading.Mutex($false, 'Global\TessynWpfE2ERun')
$owned = $false
$waitStart = [DateTime]::UtcNow
while (-not $owned) {
	try { $owned = $gate.WaitOne(15000) } catch [System.Threading.AbandonedMutexException] { $owned = $true }
	if (-not $owned) {
		Write-Host ("[Run-WpfE2E] another WPF E2E run is active; waiting ({0:N0} s)" -f ([DateTime]::UtcNow - $waitStart).TotalSeconds)
		if (([DateTime]::UtcNow - $waitStart).TotalMinutes -gt $WaitMinutes) { Write-Host '[Run-WpfE2E] gave up waiting'; exit 125 }
	}
}

# run.started marker, written first (before any cleanup or build): removed when the run ends normally (see the finally blocks), so a marker
# found at the start of a run means the previous run was interrupted (crash, reboot, killed), not failed.
$startedMarker = Join-Path $repo '.e2e-run\run.started'
New-Item -ItemType Directory -Force (Split-Path $startedMarker -Parent) | Out-Null
if (Test-Path $startedMarker) { Write-Host ("[Run-WpfE2E] NOTE: the previous run was INTERRUPTED (run.started left behind): " + ((Get-Content $startedMarker -ErrorAction SilentlyContinue) -join ' ')) }
Set-Content $startedMarker ("started {0:o} pid {1}" -f [DateTime]::UtcNow, $PID) -Encoding Ascii

if ($useTestUser) {
	$taskName = "TessynE2E-$PID"
	$runDir = Join-Path $repo ".e2e-run\$PID"
	$logFile = Join-Path $runDir 'run.log'
	$exitFile = Join-Path $runDir 'run.exit'
	$exit = 1
	$sr = $null
	$cred = $null
	try {
		Remove-StaleTestUserRuns   # we hold the mutex, so any TessynE2E-* task / account process is left over from a crashed run
		if (Test-Path (Join-Path $repo '.e2e-run')) { Get-ChildItem (Join-Path $repo '.e2e-run') -Directory | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
		$cred = Import-Clixml $credPath
		New-Item -ItemType Directory -Force $runDir | Out-Null
		$runArgs = $CommandArguments
		if ($defaultTestCommand) {
			# `dotnet test --no-build` imports the VSTest targets from the CALLER's NuGet cache (paths baked into obj\*.nuget.g.targets);
			# the test user cannot read it and `dotnet test` then silently runs nothing. `dotnet vstest <dll>` needs no NuGet targets.
			$noBuild = $false; $vs = @(); $proj = $null
			for ($i = 0; $i -lt $TestArguments.Count; $i++) {
				$a = $TestArguments[$i]
				switch -Regex ($a) {
					'^--no-build$' { $noBuild = $true }
					'^--no-restore$' { }
					'^--filter$' { $i++; $vs += "/TestCaseFilter:$($TestArguments[$i])" }
					'^--logger$' { $i++; $vs += "/Logger:$($TestArguments[$i])" }
					'^(-c|--configuration|-v|--verbosity)$' { $i++ }
					'^-' { Write-Host "[Run-WpfE2E] WARNING: option $a is not translated to vstest and is ignored in test-user mode" }
					default { $proj = $a }
				}
			}
			if (-not $proj) { $proj = Join-Path $repo 'code\TessynDesktop.WPF.E2E.Tests' }
			if ($proj -match '\.csproj$') { $proj = Split-Path $proj -Parent }
			if (-not $noBuild) {
				Write-Host "[Run-WpfE2E] building $proj as the current user"
				& $exe build $proj -nologo -v q 2>&1 | ForEach-Object { Write-Host $_ }
				if ($LASTEXITCODE -ne 0) { Write-Host '[Run-WpfE2E] build failed'; $exit = $LASTEXITCODE; throw 'build failed' }
			}
			$dll = Get-ChildItem (Join-Path $proj 'bin') -Recurse -Filter ((Split-Path $proj -Leaf) + '.dll') | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
			if (-not $dll) { throw "the test assembly of $proj was not found; build it first" }
			$runArgs = @('vstest', $dll.FullName, '/Logger:console;verbosity=normal') + $vs
		}
		$commandLine = (@((Quote $exe)) + ($runArgs | ForEach-Object { '"' + ($_ -replace '"', '""') + '"' })) -join ' '
		$cmdLines = @('@echo off', "cd /d `"$repo`"", 'set TESSYN_E2E_RUNNER=1', 'set DOTNET_NOLOGO=1', 'set DOTNET_CLI_TELEMETRY_OPTOUT=1', 'set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1')
		$cmdLines += $commandLine.Replace('%', '%%') + " > `"$logFile`" 2>&1"
		$cmdLines += "> `"$exitFile`" echo %errorlevel%"
		Set-Content (Join-Path $runDir 'run.cmd') $cmdLines -Encoding Ascii
		$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c `"$(Join-Path $runDir 'run.cmd')`""
		# A laptop: the default settings would refuse to start on battery. Time limit is a backstop; the runner enforces $TimeoutMinutes itself.
		$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes ($TimeoutMinutes + 10))
		Register-ScheduledTask -TaskName $taskName -Action $action -Settings $settings -User $cred.UserName -Password $cred.GetNetworkCredential().Password -RunLevel Limited | Out-Null
		Write-Host "[Run-WpfE2E] test-user mode: task $taskName as $($cred.UserName) (session 0, own window station); $commandLine"
		Start-ScheduledTask -TaskName $taskName
		$startedAt = [DateTime]::UtcNow
		$deadline = $startedAt.AddMinutes($TimeoutMinutes)
		$nextCheck = $startedAt.AddSeconds(15)
		$stream = $null
		$idle = 0
		while ($true) {
			if (-not $sr -and (Test-Path $logFile)) {
				$stream = New-Object IO.FileStream $logFile, 'Open', 'Read', 'ReadWrite,Delete'
				$sr = New-Object IO.StreamReader $stream
			}
			if ($sr) { while (($line = $sr.ReadLine()) -ne $null) { Write-Host $line } }
			if ((Test-Path $exitFile) -and ((Get-Content $exitFile -TotalCount 1 -ErrorAction SilentlyContinue) -match '^\s*-?\d+\s*$')) { Start-Sleep -Milliseconds 400; if ($sr) { while (($line = $sr.ReadLine()) -ne $null) { Write-Host $line } }; break }
			$now = [DateTime]::UtcNow
			if ($now -gt $deadline) { Write-Host "[Run-WpfE2E] timeout after $TimeoutMinutes min; killing the run"; $exit = 124; break }
			if ($now -gt $nextCheck) {
				$nextCheck = $now.AddSeconds(3)
				if ((Get-ScheduledTask -TaskName $taskName).State -eq 'Running') { $idle = 0 } else { $idle++ }
				if ($idle -ge 2 -and -not (Test-Path $exitFile)) {
					Write-Host '[Run-WpfE2E] the task ended without writing an exit code (killed, or the logon failed); see the Task Scheduler log'
					$exit = 1; break
				}
			}
			Start-Sleep -Milliseconds 300
		}
		if ($exit -ne 124 -and (Test-Path $exitFile)) { $txt = Get-Content $exitFile -TotalCount 1 -ErrorAction SilentlyContinue; if ($txt -match '^\s*(-?\d+)\s*$') { $exit = [int]$Matches[1] } }
	}
	catch {
		Write-Host "[Run-WpfE2E] ERROR: $($_.Exception.Message)"
		if ($exit -eq 0) { $exit = 1 }
	}
	finally {
		if ($sr) { $sr.Dispose() }
		$cred = $null
		Remove-StaleTestUserRuns
		if ((Test-Path $runDir) -and -not $env:TESSYN_E2E_KEEPRUN) { Remove-Item $runDir -Recurse -Force -ErrorAction SilentlyContinue }
		Remove-Item Env:\TESSYN_E2E_DESKTOP -ErrorAction SilentlyContinue
		Remove-Item $startedMarker -ErrorAction SilentlyContinue; if ($owned) { $gate.ReleaseMutex() }
	}
	Write-Host "[Run-WpfE2E] exit code $exit"
	exit $exit
}

$mode = if ($Hidden) { "hidden desktop '$name' (experimental, no WebView2)" } else { 'interactive desktop, serialised' }
Write-Host "[Run-WpfE2E] $mode; $commandLine"
$env:TESSYN_E2E_RUNNER = '1'
if ($Hidden) { $env:TESSYN_E2E_DESKTOP = $name } else { Remove-Item Env:\TESSYN_E2E_DESKTOP -ErrorAction SilentlyContinue }
$run = $null
$exit = 1
$sr = $null
try {
	$run = New-Object HiddenDesktopRun $name
	$run.Start($commandLine, $repo, $log)
	$deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
	$stream = New-Object IO.FileStream $log, 'Open', 'Read', 'ReadWrite,Delete'
	$sr = New-Object IO.StreamReader $stream
	while ($true) {
		$ended = $run.Wait(300)
		while (($line = $sr.ReadLine()) -ne $null) { Write-Host $line }
		if ($ended) { break }
		if ([DateTime]::UtcNow -gt $deadline) { Write-Host "[Run-WpfE2E] timeout after $TimeoutMinutes min; killing the run"; $exit = 124; break }
	}
	if ($exit -ne 124) { $exit = $run.ExitCode }
}
finally {
	if ($sr) { $sr.Dispose() }
	if ($run) { $run.Dispose() }
	Remove-Item $log -ErrorAction SilentlyContinue
	Remove-Item Env:\TESSYN_E2E_DESKTOP, Env:\TESSYN_E2E_RUNNER -ErrorAction SilentlyContinue
	Remove-Item $startedMarker -ErrorAction SilentlyContinue; if ($owned) { $gate.ReleaseMutex() }
}
Write-Host "[Run-WpfE2E] exit code $exit"
exit $exit

