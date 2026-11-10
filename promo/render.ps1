<#
  Renders promo.html slides to PNG with headless Microsoft Edge.
    docs/assets/banner.png  (README banner, 1280x640 @1.5x)
    docs/assets/og.png      (link preview, 1200x630)
    promo/out/threads-1..5.png (Threads / Instagram carousel, 1080x1350 @2x)
#>
$ErrorActionPreference = 'Stop'
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
$page = (Resolve-Path (Join-Path $PSScriptRoot 'promo.html')).Path -replace '\\', '/'
$root = Split-Path $PSScriptRoot
$profileDir = Join-Path $env:TEMP 'traykiller-headless-edge'
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'out') | Out-Null

function Shot([string]$slide, [int]$w, [int]$h, [string]$out, [double]$scale) {
    $args = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', "--user-data-dir=$profileDir",
              "--force-device-scale-factor=$scale", "--window-size=$w,$h", "--screenshot=$out", '--virtual-time-budget=6000',
              "file:///$page#$slide")
    & $edge @args 2>$null | Out-Null
    Write-Host "$slide -> $out"
}

Shot 'banner' 1280 640 (Join-Path $root 'docs\assets\banner.png') 1.5
Shot 'og' 1200 630 (Join-Path $root 'docs\assets\og.png') 1
foreach ($i in 1..5) { Shot "t$i" 1080 1350 (Join-Path $PSScriptRoot "out\threads-$i.png") 2 }
