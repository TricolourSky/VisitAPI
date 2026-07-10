# VisitAPI 0.5.1 release packager (final SPT 4.0.13 release; the Scenes pack is retired).
#   -What core   -> dist\VisitAPI-0.5.1.zip       (plugin + server framework)
#   -What source -> dist\VisitAPI-0.5.1-src.zip   (buildable GitHub source, SORA demo data stripped)
#   -What both   -> both
param([ValidateSet('core','source','both')][string]$What = 'core')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = $PSScriptRoot
$ver = '0.5.1'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Zip-Dir([string]$srcDir, [string]$zipPath) {
    if (Test-Path $zipPath) { [System.IO.File]::Delete($zipPath) }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($srcDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host ("zipped -> " + $zipPath + "  (" + [math]::Round((Get-Item $zipPath).Length/1MB,2) + " MB) done.")
}

function Build-Core {
    $c = Join-Path $dist "$ver\VisitAPI"
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

    Zip-Dir $c (Join-Path $dist "VisitAPI-$ver.zip")
}

function Build-Source {
    # Full buildable source tree. Excludes: build output (bin/obj/dist), the retired tarkin
    # asset pack (NarrateSystem), archives (_attic/_memory-backup), IDE cruft, the git-ignored
    # internal docs, and the SORA demo data (framework-only source).
    $stage = Join-Path $dist "$ver\VisitAPI-src"
    if (Test-Path $stage) { [System.IO.Directory]::Delete($stage, $true) }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    robocopy $root $stage /E /NFL /NDL /NJH /NJS /NP `
        /XD bin obj .vs .idea _memory-backup _attic NarrateSystem dist _decomp `
        /XF *.user *.suo | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0

    foreach ($internal in @("docs\DEV_NOTES.md","docs\VISITAPI_4.1_PLAN.md")) {
        $p = Join-Path $stage $internal   # git-ignored: internal, do not publish
        if (Test-Path $p) { Remove-Item $p -Force }
    }

    # framework-only source (per 0.3 convention): strip SORA demo data -> empty db + no SORA image.
    foreach ($d in @("db\assort","db\locales","db\quests")) {
        $dir = Join-Path $stage "Server\$d"
        if (Test-Path $dir) { Get-ChildItem $dir -File | Remove-Item -Force }
        Set-Content -Path "$dir\.gitkeep" -Value '' -Encoding utf8
    }
    $soraPng = Join-Path $stage "Server\images\quest\sora.png"   # keep _README.txt (framework note)
    if (Test-Path $soraPng) { Remove-Item $soraPng -Force }

    New-Item -ItemType Directory -Force -Path "$stage\dist" | Out-Null   # ship the packager itself
    Copy-Item (Join-Path $dist "package-$ver.ps1") "$stage\dist\package-$ver.ps1" -Force

    Zip-Dir $stage (Join-Path $dist "VisitAPI-$ver-src.zip")
}

if ($What -eq 'core' -or $What -eq 'both') { Build-Core }
if ($What -eq 'source' -or $What -eq 'both') { Build-Source }
