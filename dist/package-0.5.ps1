# VisitAPI 0.5 release packager.
#   -What core   -> dist\VisitAPI-0.5.zip          (plugin + server framework; light)
#   -What scenes -> dist\VisitAPI-Scenes-0.5.zip    (tarkin/bmpq MIT asset pack; ~1.4GB)
#   -What both   -> both
# Save as UTF-8 WITH BOM (PS 5.1 mangles in-file Chinese paths otherwise).
param([ValidateSet('core','scenes','source','both')][string]$What = 'core')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Zip-Dir([string]$srcDir, [string]$zipPath, [string]$level) {
    if (Test-Path $zipPath) { [System.IO.File]::Delete($zipPath) }
    $lv = if ($level -eq 'fast') { [System.IO.Compression.CompressionLevel]::Fastest } else { [System.IO.Compression.CompressionLevel]::Optimal }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($srcDir, $zipPath, $lv, $false)
    Write-Host ("zipped -> " + $zipPath + "  (" + [math]::Round((Get-Item $zipPath).Length/1MB,2) + " MB) done.")
}

function Build-Core {
    $c = Join-Path $dist "0.5\VisitAPI"
    if (Test-Path $c) { [System.IO.Directory]::Delete($c, $true) }

    $clientDll = Join-Path $root "Client\bin\Release\net472\VisitAPI.dll"
    if (-not (Test-Path $clientDll)) { $clientDll = "D:\SPT\BepInEx\plugins\VisitAPI\VisitAPI.dll" }
    $serverDll = Join-Path $root "Server\bin\Release\net9.0\VisitAPI-Server.dll"
    if (-not (Test-Path $serverDll)) { $serverDll = "D:\SPT\SPT\user\mods\VisitAPI-Server\VisitAPI-Server.dll" }

    New-Item -ItemType Directory -Force -Path `
        "$c\BepInEx\plugins\VisitAPI", `
        "$c\BepInEx\config\VisitAPI\backgrounds", `
        "$c\user\mods\VisitAPI-Server\db\assort", `
        "$c\user\mods\VisitAPI-Server\db\locales", `
        "$c\user\mods\VisitAPI-Server\db\quests", `
        "$c\user\mods\VisitAPI-Server\images\quest" | Out-Null

    Copy-Item $clientDll "$c\BepInEx\plugins\VisitAPI\VisitAPI.dll" -Force
    Copy-Item $serverDll "$c\user\mods\VisitAPI-Server\VisitAPI-Server.dll" -Force
    foreach ($d in @("db\assort","db\locales","db\quests","images\quest")) {
        Set-Content -Path "$c\user\mods\VisitAPI-Server\$d\.gitkeep" -Value '' -Encoding utf8
    }
    foreach ($f in @("README.md","README.zh-CN.md","LICENSE")) { Copy-Item (Join-Path $root $f) "$c\$f" -Force }
    Copy-Item (Join-Path $root "examples") "$c\examples" -Recurse -Force
    # docs/ intentionally NOT bundled in the release ZIP (lives in the source repo / GitHub only).

    Zip-Dir $c (Join-Path $dist "VisitAPI-0.5.zip") 'optimal'
}

function Build-Scenes {
    # tarkin/bmpq MIT pack: ship tradermod.shared.dll + bundles ONLY. NEVER tradermod.eft.dll
    # (it is bmpq's own [BepInPlugin] and would double-run vendor scenes).
    $src = Join-Path $root "NarrateSystem\BepInEx\plugins\tarkin"
    if (-not (Test-Path $src)) { throw "tarkin source pack not found at $src" }
    $s = Join-Path $dist "0.5\VisitAPI-Scenes\BepInEx\plugins\VisitAPI\scenes"
    $sroot = Join-Path $dist "0.5\VisitAPI-Scenes"
    if (Test-Path $sroot) { [System.IO.Directory]::Delete($sroot, $true) }
    New-Item -ItemType Directory -Force -Path $s | Out-Null

    Copy-Item (Join-Path $src "tradermod.shared.dll") "$s\tradermod.shared.dll" -Force
    Copy-Item (Join-Path $src "bundles") "$s\bundles" -Recurse -Force

    $credits = @(
        "VisitAPI Scenes pack",
        "",
        "3D vendor-room assets by tarkin (bmpq), from the spt-tradermod project.",
        "Licensed MIT. Bundled and redistributed here under those terms.",
        "",
        "tradermod.eft.dll is intentionally EXCLUDED: VisitAPI drives the scenes itself;",
        "including bmpq's own plugin would double-run vendor scenes and conflict."
    ) -join "`r`n"
    Set-Content -Path "$sroot\BepInEx\plugins\VisitAPI\scenes\CREDITS.txt" -Value $credits -Encoding utf8

    Zip-Dir $sroot (Join-Path $dist "VisitAPI-Scenes-0.5.zip") 'fast'
}

function Build-Source {
    # Full buildable source tree. Excludes: build output (bin/obj/dist), the ~2GB tarkin
    # asset pack (NarrateSystem), archives (_attic/_memory-backup), IDE cruft, and the
    # git-ignored internal dev notes. docs/ (minus DEV_NOTES) and Server demo data are kept.
    $stage = Join-Path $dist "0.5\VisitAPI-src"
    if (Test-Path $stage) { [System.IO.Directory]::Delete($stage, $true) }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    robocopy $root $stage /E /NFL /NDL /NJH /NJS /NP `
        /XD bin obj .vs .idea _memory-backup _attic NarrateSystem dist _decomp `
        /XF *.user *.suo | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0

    $devnotes = Join-Path $stage "docs\DEV_NOTES.md"   # git-ignored: internal, do not publish
    if (Test-Path $devnotes) { Remove-Item $devnotes -Force }

    # framework-only source (per 0.3 convention): strip SORA demo data -> empty db + no SORA image.
    foreach ($d in @("db\assort","db\locales","db\quests")) {
        $dir = Join-Path $stage "Server\$d"
        if (Test-Path $dir) { Get-ChildItem $dir -File | Remove-Item -Force }
        Set-Content -Path "$dir\.gitkeep" -Value '' -Encoding utf8
    }
    $soraPng = Join-Path $stage "Server\images\quest\sora.png"   # keep _README.txt (framework note)
    if (Test-Path $soraPng) { Remove-Item $soraPng -Force }

    New-Item -ItemType Directory -Force -Path "$stage\dist" | Out-Null   # ship the packager itself
    Copy-Item (Join-Path $dist "package-0.5.ps1") "$stage\dist\package-0.5.ps1" -Force

    Zip-Dir $stage (Join-Path $dist "VisitAPI-0.5-src.zip") 'optimal'
}

if ($What -eq 'core' -or $What -eq 'both') { Build-Core }
if ($What -eq 'scenes' -or $What -eq 'both') { Build-Scenes }
if ($What -eq 'source') { Build-Source }
