# extract-mod.ps1 — unpack an ATS/ETS2 .scs mod to plain text for inspection.
#
# The IDE file browser cannot show the ATS mod folder and drag-and-drop refuses
# .scs binaries. This bypasses both: it reads the archive straight off disk and
# writes readable .sii/.sui text to a scratch folder.
#
# Run it from anywhere; the repo root is located automatically.
#
#   .\scripts\extract-mod.ps1 -List                     # show every .scs available
#   .\scripts\extract-mod.ps1 -Mod 'Ford_F250'          # substring match, defs only
#   .\scripts\extract-mod.ps1 -Mod 'Ford_F250' -Full    # include models/textures
#   .\scripts\extract-mod.ps1 -Mod 'dlc_rt_ford' -Game  # read the live game copy
#
# Both the mod folder and the game install are searched, because they are NOT
# the same data. A DLC left sitting in the mod folder is often an older build
# than the copy the game actually loads, so reading the mod folder can quietly
# answer a question with stale content. When one name exists in both places the
# script says so instead of silently picking one.

[CmdletBinding()]
param(
    [string]$Mod,
    [switch]$Full,
    [switch]$List,
    [switch]$Game,          # restrict the search to the game install
    [string]$GameDir,       # override the auto-detected game directory
    [string]$OutRoot = (Join-Path $env:TEMP 'ats-work'),
    [string]$Repo
)

$ErrorActionPreference = 'Stop'

# Walk up from the script location until the CLI project (i.e. the repo root) is found,
# so the helper keeps working from %TEMP%, scripts\ or the repository root.
if (-not $Repo) {
    $dir = Split-Path -Parent $PSCommandPath
    while ($dir -and -not (Test-Path (Join-Path $dir 'Cli\ATSRoadTripConverter.Cli.csproj'))) {
        $parent = Split-Path -Parent $dir
        if (-not $parent -or $parent -eq $dir) { throw "Could not locate the repo root from $PSCommandPath" }
        $dir = $parent
    }
    $Repo = $dir
}

$modDir = Join-Path $env:USERPROFILE 'Documents\American Truck Simulator\mod'

if (-not (Test-Path $modDir)) { throw "Mod folder not found: $modDir" }

# Locate the game install so live DLC archives can be read directly. Steam library
# folders are consulted because the install is usually not under Program Files.
function Resolve-GameDir {
    param([string]$Override)

    if ($Override) {
        if (-not (Test-Path $Override)) { throw "Game directory not found: $Override" }
        return (Resolve-Path $Override).Path
    }

    $steamRoots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Steam'),
        (Join-Path $env:ProgramFiles 'Steam')
    ) | Where-Object { $_ }

    $vdf = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' -AllMatches | ForEach-Object {
            foreach ($m in $_.Matches) { $steamRoots += $m.Groups[1].Value }
        }
    }

    # Two passes, not one nested loop: every root is searched for ATS before any
    # root is searched for ETS2. A single loop would return whichever game sits on
    # the alphabetically first library, which on a multi-library machine is
    # routinely the wrong game entirely.
    foreach ($game in @('American Truck Simulator', 'Euro Truck Simulator 2')) {
        foreach ($root in ($steamRoots | Sort-Object -Unique)) {
            $p = Join-Path (Join-Path $root 'steamapps\common') $game
            if (Test-Path $p) { return (Resolve-Path $p).Path }
        }
    }

    return $null
}

$gameDir = Resolve-GameDir -Override $GameDir

# Collect candidates from both locations, tagged with where each came from, so a
# duplicate name in the mod folder and the game install never resolves silently.
$candidates = @()
$candidates += @(Get-ChildItem $modDir -File | ForEach-Object {
    [pscustomobject]@{ Name = $_.Name; Path = $_.FullName; Source = 'mod folder'; Length = $_.Length }
})
if ($gameDir) {
    $candidates += @(Get-ChildItem $gameDir -File -Filter '*.scs' | ForEach-Object {
        [pscustomobject]@{ Name = $_.Name; Path = $_.FullName; Source = 'game install'; Length = $_.Length }
    })
}

# Same name in both places does not mean same content. Size first, hash only when
# the sizes agree, so a routine -List stays fast on multi-gigabyte archives.
function Test-SameArchive {
    param($A, $B)
    if ($A.Length -ne $B.Length) { return $false }
    try {
        return (Get-FileHash $A.Path -Algorithm SHA256).Hash -eq (Get-FileHash $B.Path -Algorithm SHA256).Hash
    } catch { return $true }
}

if ($List) {
    $rows = @()
    foreach ($c in ($candidates | Sort-Object Name)) {
        $other = $candidates | Where-Object { $_.Name -eq $c.Name -and $_.Path -ne $c.Path } | Select-Object -First 1
        $note = ''
        if ($other) {
            $note = if (Test-SameArchive $c $other) { 'same as other copy' } else { 'DIFFERS from other copy' }
        }
        $rows += [pscustomobject]@{
            Name   = $c.Name
            MB     = [math]::Round($c.Length / 1MB, 1)
            Bytes  = $c.Length
            Source = $c.Source
            Note   = $note
        }
    }
    $rows | Format-Table -AutoSize
    if ($rows.Note -contains 'DIFFERS from other copy') {
        Write-Host "Some archives exist in both locations with different content." -ForegroundColor Yellow
        Write-Host "Read those with -Game to get the copy the game actually loads." -ForegroundColor Yellow
    }
    return
}

if (-not $Mod) { throw "Pass -Mod <name> (or -List to see what is available)." }

$matches = @($candidates | Where-Object { $_.Name -like "*$Mod*" })
if ($matches.Count -eq 0) {
    $where = if ($gameDir) { "$modDir or $gameDir" } else { $modDir }
    throw "No archive matching '$Mod' in $where"
}

if ($Game) {
    if (-not $gameDir) { throw "-Game was requested but no game install was found. Pass -GameDir <path>." }
    $fromGame = @($matches | Where-Object { $_.Source -eq 'game install' })
    if ($fromGame.Count -eq 0) { throw "-Game was requested but '$Mod' is not in the game install." }
    $matches = $fromGame
}

if ($matches.Count -gt 1) {
    Write-Host "Multiple matches - pick one:" -ForegroundColor Yellow
    $matches | ForEach-Object {
        Write-Host ("  {0}  [{1}, {2} MB]" -f $_.Name, $_.Source, [math]::Round($_.Length / 1MB, 1))
    }
    Write-Host "Re-run with -Game to force the game install copy." -ForegroundColor DarkGray
    return
}

$archive = $matches[0]

# The guard: a name present in both locations with differing bytes is almost
# always a stale leftover. Say so before extracting, not after being misled.
$shadow = $candidates | Where-Object { $_.Name -eq $archive.Name -and $_.Path -ne $archive.Path } | Select-Object -First 1
if ($shadow -and -not (Test-SameArchive $archive $shadow)) {
    Write-Host "WARNING: '$($archive.Name)' exists in both locations with different content." -ForegroundColor Yellow
    Write-Host "  reading : $($archive.Path) ($([math]::Round($archive.Length/1MB,1)) MB)" -ForegroundColor Yellow
    Write-Host "  other   : $($shadow.Path) ($([math]::Round($shadow.Length/1MB,1)) MB)" -ForegroundColor Yellow
    Write-Host "  This copy may be an older build than the one the game loads." -ForegroundColor Yellow
    if (-not $Game) { Write-Host "  Re-run with -Game to read the game install copy instead.`n" -ForegroundColor Yellow }
}
$safe    = ([System.IO.Path]::GetFileNameWithoutExtension($archive.Name) -replace '[^\w\.\-]', '_')
$dest    = Join-Path $OutRoot $safe

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }

$cliArgs = @('run', '--project', 'Cli', '-c', 'Release', '--',
             '--extract', $archive.Path, '--output', $dest)
if (-not $Full) { $cliArgs += '--defs-only' }

Write-Host "Extracting : $($archive.Name) ($([math]::Round($archive.Length/1MB,1)) MB)"
Write-Host "Into       : $dest"
Write-Host "Mode       : $(if ($Full) { 'full' } else { 'defs-only' })`n"

Push-Location $Repo
try { & dotnet @cliArgs } finally { Pop-Location }

if ($LASTEXITCODE -ne 0) { throw "Extraction failed (exit $LASTEXITCODE)." }

$files = Get-ChildItem $dest -Recurse -File
Write-Host "`nExtracted $($files.Count) file(s)." -ForegroundColor Green
Write-Host "Read them by absolute path, e.g.:"
$files | Where-Object { $_.Name -eq 'data.sii' } | Select-Object -First 1 |
    ForEach-Object { Write-Host "  $($_.FullName)" }