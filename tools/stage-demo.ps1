<#
  Stages a safe demo of Tray Killer for testing and screenshots.

  - Builds a few harmless fake tray apps ("Remote Agent Lite", "UpdateNagger", …) from tools\DemoTray
  - Gives them real auto-start entries (HKCU Run, Startup folder shortcut, a daily scheduled task)
  - Starts Tray Killer in an isolated "demo" profile (its own settings/data, never touches your real rules)

  Usage:   .\tools\stage-demo.ps1            # stage + launch
           .\tools\stage-demo.ps1 -Cleanup   # stop everything and remove every trace
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\TrayKiller\bin\Release\TrayKiller.exe'),
    [string]$WorkDir = (Join-Path $env:ProgramData 'TrayKillerDemo'),
    [switch]$Elevated,
    [switch]$Cleanup,
    [switch]$SeedHistory
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$profileDir = Join-Path $env:LOCALAPPDATA 'TrayKiller\profiles\demo'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startup = [Environment]::GetFolderPath('Startup')
$taskName = 'RemoteAgentLite Updater (Tray Killer demo)'

$apps = @(
    @{ File = 'RemoteAgentLite.exe'; Name = 'Remote Agent Lite'; Tip = 'Remote Agent Lite — connected'; Color = '10B981'; Letter = 'R' },
    @{ File = 'UpdateNagger.exe';    Name = 'UpdateNagger';      Tip = 'UpdateNagger: 3 updates available'; Color = '3B82F6'; Letter = 'U' },
    @{ File = 'AdPopper.exe';        Name = 'AdPopper';          Tip = 'AdPopper — Deals of the day!'; Color = 'F59E0B'; Letter = 'A' },
    @{ File = 'PromoTray.exe';       Name = 'PromoTray';         Tip = 'PromoTray'; Color = '8B5CF6'; Letter = 'P' }
)

function Stop-Demo {
    $running = Get-CimInstance Win32_Process -Filter "Name='TrayKiller.exe'" | Where-Object { $_.CommandLine -match '--profile\s+demo' }
    if ($running) {
        & $Exe --profile demo --cmd exit
        Start-Sleep -Milliseconds 1500
        $running | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    }
    foreach ($a in $apps) { Get-Process ([IO.Path]::GetFileNameWithoutExtension($a.File)) -ErrorAction SilentlyContinue | Stop-Process -Force }
    foreach ($a in $apps) { Remove-ItemProperty $runKey -Name ([IO.Path]::GetFileNameWithoutExtension($a.File)) -ErrorAction SilentlyContinue }
    Get-ChildItem $startup -Filter 'PromoTray*.lnk' -ErrorAction SilentlyContinue | Remove-Item -Force
    schtasks /Delete /TN $taskName /F 2>$null | Out-Null
    # forget the demo apps in Windows 11's tray icon settings
    Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' -ErrorAction SilentlyContinue | ForEach-Object {
        $p = (Get-ItemProperty $_.PSPath).ExecutablePath
        if ($p -and $p -like "*TrayKillerDemo*") { Remove-Item $_.PSPath -Recurse -Force }
    }
}

if ($Cleanup) {
    Stop-Demo
    Remove-Item $profileDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Demo removed.'
    return
}

Stop-Demo
Remove-Item $profileDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $WorkDir 'apps'), $profileDir | Out-Null

# 1) build the fake apps, each with its own icon
$csc = (Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe" | Select-Object -First 1).FullName
$src = Join-Path $PSScriptRoot 'DemoTray\DemoTray.cs'
$tool = Join-Path $WorkDir 'DemoTray.exe'
& $csc /nologo /target:winexe /optimize+ "/out:$tool" /r:System.Windows.Forms.dll /r:System.Drawing.dll $src
foreach ($a in $apps) {
    $ico = Join-Path $WorkDir ($a.Letter + '.ico')
    & $tool --ico $ico $a.Color $a.Letter
    & $csc /nologo /target:winexe /optimize+ "/out:$(Join-Path $WorkDir "apps\$($a.File)")" "/win32icon:$ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll $src
}

# 2) seed the demo profile with rules
$rules = foreach ($a in $apps) {
    [ordered]@{
        Id = [guid]::NewGuid().ToString('N'); Name = $a.Name; Pattern = $a.File.ToLower(); Path = (Join-Path $WorkDir "apps\$($a.File)")
        Enabled = $true; KillProcess = $true; BlockAutostart = $true; HideTrayIcon = $true; DisableServices = ($a.Letter -eq 'R')
        KillCount = 0; AllowKeys = @()
    }
}
$settings = [ordered]@{
    SweepSeconds = 10; RealtimeWatch = $true; CleanTrayIcons = $true; NotifyOnKill = $false; StartWithWindows = $true
    AutoUpdate = $true; Paused = $false; Language = 'auto'; SetupDone = $true; TrayHintShown = $true; SelfPromoted = $true; Rules = @($rules)
}
$settings | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $profileDir 'settings.json') -Encoding utf8
if ($SeedHistory) {
    # sample history for the 7-day chart (demo profile only)
    $daily = [ordered]@{}; $total = 0
    $sample = @(9, 14, 6, 11, 17, 8)
    for ($i = 6; $i -ge 1; $i--) { $n = $sample[6 - $i]; $daily[(Get-Date).AddDays(-$i).ToString('yyyy-MM-dd')] = $n; $total += $n }
    [ordered]@{ TotalKills = $total; TotalBlocked = 0; Daily = $daily } | ConvertTo-Json | Set-Content (Join-Path $profileDir 'stats.json') -Encoding utf8
}

# 3) give the fake apps real auto-start entries
foreach ($a in $apps | Where-Object { $_.Letter -in 'U', 'A' }) {
    $exePath = Join-Path $WorkDir "apps\$($a.File)"
    Set-ItemProperty $runKey -Name ([IO.Path]::GetFileNameWithoutExtension($a.File)) -Value "`"$exePath`" `"$($a.Tip)`" $($a.Color) $($a.Letter)"
}
$promo = $apps | Where-Object { $_.Letter -eq 'P' }
$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut((Join-Path $startup 'PromoTray.lnk'))
$lnk.TargetPath = Join-Path $WorkDir "apps\$($promo.File)"; $lnk.Arguments = "`"$($promo.Tip)`" $($promo.Color) $($promo.Letter)"; $lnk.Save()
$remote = $apps | Where-Object { $_.Letter -eq 'R' }
schtasks /Create /TN $taskName /TR "`"$(Join-Path $WorkDir "apps\$($remote.File)")`"" /SC DAILY /ST 09:00 /F | Out-Null

# 4) start the fake apps, then Tray Killer
foreach ($a in $apps) {
    Start-Process (Join-Path $WorkDir "apps\$($a.File)") -ArgumentList "`"$($a.Tip)`"", $a.Color, $a.Letter
}
Start-Sleep -Seconds 3
if ($Elevated) { Start-Process $Exe -ArgumentList '--profile', 'demo' -Verb RunAs }
else { Start-Process $Exe -ArgumentList '--profile', 'demo', '--no-elevate' }
Write-Host "Demo staged. Fake apps in $WorkDir\apps, profile in $profileDir"
