<#
  Builds TrayKiller.exe into .\dist together with its SHA-256 checksum (used by the auto-updater).

  .\build.ps1                     # version 1.0.0
  .\build.ps1 -Version 1.2.3      # stamp a version
  .\build.ps1 -UseRefPackages     # no local .NET Framework targeting pack needed (CI)
#>
param(
    [string]$Version = '1.0.0',
    [string]$Configuration = 'Release',
    [switch]$UseRefPackages
)
$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found. Install Visual Studio (or Build Tools) with the .NET desktop workload.' }

$project = Join-Path $PSScriptRoot 'src\TrayKiller\TrayKiller.csproj'
$props = @("/p:Configuration=$Configuration", "/p:AppVersion=$Version")
if ($UseRefPackages) { $props += '/p:UseRefPackages=true' }
& $msbuild $project /restore /t:Rebuild @props /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$exe = Join-Path $dist 'TrayKiller.exe'
Copy-Item (Join-Path $PSScriptRoot "src\TrayKiller\bin\$Configuration\TrayKiller.exe") $exe -Force
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$exe.sha256", "$hash  TrayKiller.exe`n")
Write-Host "Built $exe (v$Version)"
Write-Host "SHA-256 $hash"
