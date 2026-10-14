[CmdletBinding()]
param([string]$GameDir = (Join-Path $PSScriptRoot '..\..\Game'),[switch]$WorldRegression,[switch]$Mission,[switch]$MissionExits,[switch]$Urine,[switch]$UrineParts,[switch]$Performance,[ValidateRange(0,360)][int]$PerformanceFrameRate=0,[switch]$FocusInput,[switch]$ActorParts,[switch]$Actors,[switch]$JoinInput,[switch]$Combat,[switch]$Doors,[switch]$DoorReload,[switch]$MissingHandle,[switch]$State,[switch]$Audit,[string[]]$AuditLevels,[string]$Level)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue) { throw 'Close the game before the two-instance travel test.' }
$output = Join-Path $repo ('.local\travel-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$clientDir = Join-Path $repo '.local\travel-client'
New-Item -ItemType Directory -Force -Path $output,$clientDir | Out-Null
# A separate executable directory provides separate loader logs/config/cache. Game data is shared locally.
foreach ($file in Get-ChildItem -LiteralPath $GameDir -File) {
    $target = Join-Path $clientDir $file.Name
    if (!(Test-Path -LiteralPath $target)) { New-Item -ItemType HardLink -Path $target -Target $file.FullName | Out-Null }
}
foreach ($folder in @('POSTAL Brain Damaged_Data', 'MonoBleedingEdge', 'dotnet')) {
    $source = Join-Path $GameDir $folder
    $target = Join-Path $clientDir $folder
    if ((Test-Path -LiteralPath $source) -and !(Test-Path -LiteralPath $target)) { New-Item -ItemType Junction -Path $target -Target $source | Out-Null }
}
$clientBep = Join-Path $clientDir 'BepInEx'
New-Item -ItemType Directory -Force -Path $clientBep | Out-Null
foreach ($folder in @('core','interop')) {
    $target = Join-Path $clientBep $folder
    if (!(Test-Path -LiteralPath $target)) { New-Item -ItemType Junction -Path $target -Target (Join-Path $GameDir "BepInEx\$folder") | Out-Null }
}
foreach ($folder in @('cache','config')) {
    $source = Join-Path $GameDir "BepInEx\$folder"
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $clientBep -Recurse -Force }
}
$plugins = Join-Path $clientBep 'plugins\PBDMP'
$clientConfig = Join-Path $clientBep 'config\BepInEx.cfg'
$cfg = Get-Content -LiteralPath $clientConfig -Raw
$cfg.Replace('UpdateInteropAssemblies = true','UpdateInteropAssemblies = false') | Set-Content -LiteralPath $clientConfig
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item -LiteralPath (Join-Path $GameDir 'BepInEx\plugins\PBDMP\PBDMP.dll'),(Join-Path $GameDir 'BepInEx\plugins\PBDMP\PBDMP.Network.dll') -Destination $plugins -Force
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$probe.Start(); $port = $probe.LocalEndpoint.Port; $probe.Stop()
$previousRole = $env:PBDMP_TRAVEL_TEST; $previousOutput = $env:PBDMP_TRAVEL_OUTPUT; $previousPort = $env:PBDMP_TRAVEL_PORT
$previousCombat = $env:PBDMP_SHARED_COMBAT_TEST
$previousWorldRegression=$env:PBDMP_WORLD_REGRESSION
$env:PBDMP_WORLD_REGRESSION=if($WorldRegression){'1'}else{$null}
$previousUrineParts=$env:PBDMP_URINE_PARTS_TEST
$env:PBDMP_URINE_PARTS_TEST=if($UrineParts){'1'}else{$null}
$previousUrine=$env:PBDMP_URINE_TEST
$env:PBDMP_URINE_TEST=if($Urine -or $Mission){'1'}else{$null}
$previousMissionExits=$env:PBDMP_MISSION_EXITS_TEST
$env:PBDMP_MISSION_EXITS_TEST=if($MissionExits){'1'}else{$null}
$previousMission=$env:PBDMP_MISSION_TEST
$env:PBDMP_MISSION_TEST=if($Mission){'1'}else{$null}
$previousDoors = $env:PBDMP_DOOR_TEST
$previousJoinInput = $env:PBDMP_JOIN_INPUT_TEST
$previousActors = $env:PBDMP_ACTOR_TEST
$env:PBDMP_ACTOR_TEST = if($Actors) {'1'} else {$null}
$previousActorParts = $env:PBDMP_ACTOR_PART_TEST
$env:PBDMP_ACTOR_PART_TEST = if($ActorParts -or $UrineParts) {'1'} else {$null}
$env:PBDMP_JOIN_INPUT_TEST = if($JoinInput) {'1'} else {$null}
$previousDoorReload = $env:PBDMP_DOOR_RELOAD_TEST
$env:PBDMP_DOOR_RELOAD_TEST = if($DoorReload) {'1'} else {$null}
$previousMissingHandle = $env:PBDMP_DOOR_MISSING_HANDLE
$env:PBDMP_DOOR_MISSING_HANDLE = if($MissingHandle) {'1'} else {$null}
 $previousState = $env:PBDMP_STATE_TEST
 $env:PBDMP_STATE_TEST = if($State) {'1'} else {$null}
$env:PBDMP_DOOR_TEST = if($Doors) {'1'} else {$null}
$previousAudit = $env:PBDMP_CAMPAIGN_AUDIT
$previousAuditLevels = $env:PBDMP_AUDIT_LEVELS
$previousLevel = $env:PBDMP_TEST_LEVEL
$env:PBDMP_TEST_LEVEL = $Level
$env:PBDMP_AUDIT_LEVELS = if($AuditLevels) { $AuditLevels -join ',' } else { $null }
$env:PBDMP_CAMPAIGN_AUDIT = if ($Audit) { '1' } else { $null }
$env:PBDMP_SHARED_COMBAT_TEST = if ($Combat) { '1' } else { $null }
$env:PBDMP_TRAVEL_OUTPUT = $output; $env:PBDMP_TRAVEL_PORT = $port
# A Steam install started directly relaunches itself through Steam unless the app id is already in its environment.
$previousSteamApp = $env:SteamAppId
if (!$env:SteamAppId -and !(Test-Path -LiteralPath (Join-Path $GameDir 'steam_appid.txt'))) { $env:SteamAppId = '1359980' }
$players = @()
$previousPerf=$env:PBDMP_SYNC_PERF_TEST
$previousPerfFrameRate=$env:PBDMP_SYNC_PERF_RATE
$env:PBDMP_SYNC_PERF_RATE=if($Performance -and $PerformanceFrameRate){[string]$PerformanceFrameRate}else{$null}
$env:PBDMP_SYNC_PERF_TEST=if($Performance){'1'}else{$null}
if($Performance){$env:PBDMP_JOIN_INPUT_TEST='1'}
$previousFocusInput=$env:PBDMP_FOCUS_INPUT_TEST
$env:PBDMP_FOCUS_INPUT_TEST=if($FocusInput){'1'}else{$null}
if($FocusInput) {
    $env:PBDMP_JOIN_INPUT_TEST='1'
    if(!('PbdmpFocusProbe' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class PbdmpFocusProbe {
 private delegate bool EnumWindow(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow f,IntPtr p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a,uint b,bool attach);
 [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
 [DllImport("user32.dll")] private static extern IntPtr SetActiveWindow(IntPtr h);
 [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,int x,int y,uint d,UIntPtr e);
 public static IntPtr Window(uint process) {
  IntPtr result=IntPtr.Zero;
  EnumWindows((h,p)=>{uint id;GetWindowThreadProcessId(h,out id);var c=new StringBuilder(128);GetClassName(h,c,128);
   if(id==process && c.ToString()=="UnityWndClass") {result=h;return false;}return true;},IntPtr.Zero);
  if(result==IntPtr.Zero) throw new Exception("Owned Unity window not found.");return result;
 }
 public static void Activate(uint process) {
  var h=Window(process);ShowWindow(h,9);uint ignored;
  var foreground=GetWindowThreadProcessId(GetForegroundWindow(),out ignored);var thread=GetCurrentThreadId();
  bool attached=foreground!=0 && foreground!=thread && AttachThreadInput(thread,foreground,true);
  var target=GetWindowThreadProcessId(h,out ignored);bool targetAttached=target!=thread && target!=foreground && AttachThreadInput(thread,target,true);
  try{BringWindowToTop(h);SetActiveWindow(h);SetForegroundWindow(h);SetFocus(h);}
  finally{if(targetAttached)AttachThreadInput(thread,target,false);if(attached)AttachThreadInput(thread,foreground,false);}
 }
}
'@
    }
}
$focusTestDone=$false
try {
    $roles = if($Audit) { @('host') } else { @('host','client') }
    foreach ($role in $roles) {
        if ($role -eq 'client') {
            $wait = [Diagnostics.Stopwatch]::StartNew()
            while (!(Test-Path -LiteralPath (Join-Path $output 'host-ready'))) {
                foreach($owned in $players) {$owned.Refresh();if($owned.HasExited){throw "P1 test process exited during initialization: $($owned.ExitCode)"}}
                if ($wait.Elapsed.TotalSeconds -gt 90) { throw 'P1 campaign initialization timed out.' }
                $hostLog = Join-Path $GameDir 'BepInEx\LogOutput.log'
                if ((Test-Path -LiteralPath $hostLog) -and (Get-Content -LiteralPath $hostLog -Raw) -match 'PBDMP stopped after runtime error|Error loading \[PBDMP') { throw 'P1 failed; inspect loader log.' }
                Start-Sleep -Seconds 1
            }
        }
        $env:PBDMP_TRAVEL_TEST = $role
        $launchDir = if ($role -eq 'host') { $GameDir } else { $clientDir }
        $loaderLog = Join-Path $launchDir 'BepInEx\LogOutput.log'
        if (Test-Path -LiteralPath $loaderLog) {
            Move-Item -LiteralPath $loaderLog -Destination (Join-Path $output ("previous-$role-loader.log"))
        }
        $players += Start-Process -FilePath (Join-Path $launchDir 'POSTAL Brain Damaged.exe') -WorkingDirectory $launchDir -WindowStyle Hidden -PassThru -ArgumentList '-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"' + (Join-Path $output "$role-unity.log") + '"')
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path -LiteralPath (Join-Path $output 'host-pass')) -or (!$Audit -and !(Test-Path -LiteralPath (Join-Path $output 'client-pass')))) {
        if($FocusInput -and !$focusTestDone -and (Test-Path -LiteralPath (Join-Path $output 'client-focus-ready'))) {
            $focusTestDone=$true
            [PbdmpFocusProbe]::Activate($players[1].Id)
            Start-Sleep -Milliseconds 700
            for($cycle=0;$cycle -lt 5;$cycle++) {
                foreach($owned in $players) {
                    [PbdmpFocusProbe]::Activate($owned.Id)
                    Start-Sleep -Milliseconds 700
                    [uint32]$foregroundProcess=0
                    [PbdmpFocusProbe]::GetWindowThreadProcessId([PbdmpFocusProbe]::GetForegroundWindow(),[ref]$foregroundProcess)|Out-Null
                    if($foregroundProcess -ne $owned.Id) {throw "Focus test could not activate the owned game window (wanted $($owned.Id), foreground $foregroundProcess)."}
                }
            }
            try {
                [PbdmpFocusProbe]::keybd_event(0x57,0,0,[UIntPtr]::Zero)
                for($sample=0;$sample -lt 10;$sample++) {
                    [uint32]$foregroundProcess=0
                    [PbdmpFocusProbe]::GetWindowThreadProcessId([PbdmpFocusProbe]::GetForegroundWindow(),[ref]$foregroundProcess)|Out-Null
                    if($foregroundProcess -ne $players[1].Id) {throw 'Focus test lost the owned client window before input injection.'}
                    [PbdmpFocusProbe]::mouse_event(1,8,0,0,[UIntPtr]::Zero)
                    Start-Sleep -Milliseconds 100
                }
            } finally {[PbdmpFocusProbe]::keybd_event(0x57,0,2,[UIntPtr]::Zero)}
            'pass'|Set-Content -LiteralPath (Join-Path $output 'focus-switches-done')
        }
        $limit = if($Audit) { 1800 } elseif($Actors -or $ActorParts) {600} else { 150 }
        if ($watch.Elapsed.TotalSeconds -gt $limit) { throw "Native game test timed out; inspect $output" }
        foreach ($game in $players) { $game.Refresh(); if ($game.HasExited) { throw "Owned test process exited: $($game.ExitCode)" } }
        $activeLogs = if($Audit) { @((Join-Path $GameDir 'BepInEx\LogOutput.log')) } else { @((Join-Path $GameDir 'BepInEx\LogOutput.log'),(Join-Path $clientBep 'LogOutput.log')) }
        foreach ($logPath in $activeLogs) {
            if ((Test-Path -LiteralPath $logPath) -and (Get-Content -LiteralPath $logPath -Raw) -match 'PBDMP stopped after runtime error|Campaign session failed|Error loading \[PBDMP|During invoking native->managed trampoline|Exception in IL2CPP-to-Managed trampoline') { throw "Travel failure; inspect $logPath and $output" }
        }
        Start-Sleep -Seconds 1
    }
    $description = if ($WorldRegression) {'native hit particles, powered urine, barrel-linked geometry and special explosion replay'} elseif ($MissionExits) {'both-player exits and deferred automatic story endings'} elseif($Mission) {'actual Neighborhood wall destruction, native passage and late join'} elseif ($Audit) {'campaign catalogue/state coverage'} elseif($ActorParts) {'native boss weak spots, balloon targets and scripted shoot-reactive humans'} elseif($Actors) {'native campaign actor family spawn/status/damage/death lifecycles'} elseif($Performance) {'steady host state-capture frame budget and native join/pause input'} elseif($FocusInput) {'overlapping pause locks, real window focus changes and native keyboard/mouse input'} elseif($JoinInput) {'immediate main-menu join controls, shared pause/resume and modal input cleanup'} elseif ($DoorReload) {'solo start, host/join, reload and both-player bedroom door interactions'} elseif ($State) {'native doors and encounter late-join state'} elseif ($Doors) {'requester-owned keys and shared unlock'} elseif ($Combat) { 'shared enemy authority and personal damage' } else { 'campaign travel/respawn' }
    Write-Host "Native game diagnostics passed: $description; $output"
} finally {
    $env:PBDMP_WORLD_REGRESSION=$previousWorldRegression
    $env:PBDMP_MISSION_EXITS_TEST=$previousMissionExits
    $env:PBDMP_MISSION_TEST=$previousMission
    foreach ($game in $players) { $game.Refresh(); if (!$game.HasExited) { Stop-Process -Id $game.Id; $game.WaitForExit(5000) | Out-Null } }
    $env:PBDMP_TRAVEL_TEST = $previousRole; $env:PBDMP_TRAVEL_OUTPUT = $previousOutput; $env:PBDMP_TRAVEL_PORT = $previousPort
    $env:SteamAppId = $previousSteamApp
    $env:PBDMP_SHARED_COMBAT_TEST = $previousCombat
    $env:PBDMP_URINE_TEST=$previousUrine
    $env:PBDMP_URINE_PARTS_TEST=$previousUrineParts
    $env:PBDMP_DOOR_TEST = $previousDoors
    $env:PBDMP_JOIN_INPUT_TEST = $previousJoinInput
    $env:PBDMP_SYNC_PERF_TEST=$previousPerf
    $env:PBDMP_SYNC_PERF_RATE=$previousPerfFrameRate
    $env:PBDMP_FOCUS_INPUT_TEST=$previousFocusInput
    $env:PBDMP_ACTOR_TEST = $previousActors
    $env:PBDMP_ACTOR_PART_TEST = $previousActorParts
    $env:PBDMP_DOOR_RELOAD_TEST = $previousDoorReload
    $env:PBDMP_DOOR_MISSING_HANDLE = $previousMissingHandle
    $env:PBDMP_STATE_TEST = $previousState
    $env:PBDMP_CAMPAIGN_AUDIT = $previousAudit
    $env:PBDMP_AUDIT_LEVELS = $previousAuditLevels
    $env:PBDMP_TEST_LEVEL = $previousLevel
    foreach ($pair in @(@($GameDir,'host'),@($clientDir,'client'))) {
        $logPath = Join-Path $pair[0] 'BepInEx\LogOutput.log'
        if (Test-Path -LiteralPath $logPath) { Copy-Item -LiteralPath $logPath -Destination (Join-Path $output ($pair[1] + '-bepinex.log')) -Force }
    }
    $output | Set-Content -LiteralPath (Join-Path $repo '.local\last-travel-test.txt')
}
