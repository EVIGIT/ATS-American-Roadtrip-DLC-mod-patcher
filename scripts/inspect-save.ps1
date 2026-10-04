# inspect-save.ps1 - print the SHAPE of an ATS/ETS2 .sii file, folder or .scs archive.
#
# A save or a game archive cannot be attached in a conversation, and pasting one whole would put
# a real profile's progress into a chat log. This prints the structure only: the key hierarchy
# with every value replaced by a type hint. That is what a parser has to be written against, and
# it discloses nothing about the profile.
#
# RUN FROM THE REPO ROOT, or give the full path to the script. A relative path only resolves
# from the repo folder:
#
#   cd /d "C:\Users\weesc\Downloads\ATS-American-Roadtrip-Car-Patcher-v12\ATS-American-Roadtrip-Car-Patcher-v12"
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\inspect-save.ps1 -Raw -Filter car_dealer -Path "D:\SteamLibrary\steamapps\common\American Truck Simulator\base.scs"
#
# -ExecutionPolicy Bypass is required on a stock Windows install, which blocks running .ps1
# files by default. Without it: "running scripts is disabled on this system".
#
# Three inputs, all read in place with no manual extraction:
#   1. a single .sii / .sui file  e.g. ...\save\autosave_drive_9\game.sii
#   2. a folder                  every .sii/.sui beneath it
#   3. an .scs / .zip archive     read from inside the zip
#
# Options:
#   -MaxDepth <n>  how deep to descend (default 4)
#   -Filter <text> only archive entries whose path contains this
#   -Focus         only keys matching the save editor's feature groups
#   -Match <regex> only keys matching your own pattern
#   -Raw          print values too. Only for something you do not mind sharing.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [int]$MaxDepth = 4,
    [string]$Filter,
    [switch]$Focus,
    [string]$Match,
    [switch]$Raw
)

$ErrorActionPreference = 'Stop'

# -Focus narrows to the three groups the save editor cares about. -Match overrides it. Neither
# means "print the whole structure".
$FocusPattern = $null
if ($Match) {
    $FocusPattern = $Match
} elseif ($Focus) {
    $FocusPattern = 'money|xp|experience|skill|point|city|cities|truck|unlock|garage|level|profile'
}

function Get-ValueHint {
    # $Raw is passed in rather than read from the caller's scope. Relying on dynamic
    # scoping silently produced raw values with -Raw switched off, which defeats the
    # whole point of the script.
    param([string]$Value, [bool]$ShowValues)

    if ($ShowValues) {
        if ($Value.Length -gt 60) { return $Value.Substring(0, 60) + ' ...' }
        return $Value
    }
    $v = $Value.Trim()
    if ($v -eq '') { return '<empty>' }
    if ($v.StartsWith('"')) { return '<string:' + $v.Length + ' chars>' }
    if ($v -match '^-?\d+$') { return '<number>' }
    if ($v -match '^-?\d+\.\d+$') { return '<decimal>' }
    if ($v -match '^[A-Za-z0-9+/=]{40,}$') { return '<blob:' + $v.Length + ' chars>' }
    return '<value:' + $v.Length + ' chars>'
}

function Get-SiiLines {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [int]$Depth,
        [string]$Pattern,
        [bool]$ShowValues
    )

    $out = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $seen = @{}

    # Held candidate for "key: value" whose brace may arrive on the next line. SCS writes a
    # unit declaration that way:
    #     car_dealer: .tdealer.s63
    #     {
    # but an ordinary leaf ("odometer: 0") looks identical until the next line is read, so
    # the candidate is held and only promoted to a block if a brace actually follows.
    # Anything else flushes it as a plain leaf.
    $holdKey = $null
    $holdValue = $null

    function Add-Block([string]$shown) {
        if ($seen.ContainsKey($shown)) {
            $seen[$shown]++
            if ($seen[$shown] -le 2) {
                $out.Add($shown + '   (repeat ' + $seen[$shown] + ')')
            }
        } else {
            $seen[$shown] = 1
            $out.Add($shown)
        }
    }

    foreach ($raw in ($Text -split "`r?`n")) {
        $t = $raw.Trim()
        if ($t -eq '' -or $t.StartsWith('//') -or $t -eq 'SiiNunit' -or $t.StartsWith('#')) { continue }

        $d = $stack.Count
        $pad = '  ' * $d

        # A brace right after a held candidate makes that candidate a declaration.
        if ($t -eq '{' -and $holdKey) {
            $label = $holdKey
            if ($null -ne $holdValue) { $label = $holdKey + ': ' + $holdValue }
            $holdKey = $null
            $holdValue = $null
            $stack.Push($label)
            if ($d -gt $Depth) { continue }
            if ($Pattern -and $label -notmatch $Pattern) { continue }
            Add-Block ($pad + $label)
            continue
        }

        # Otherwise the held candidate was an ordinary leaf. Emit it, then handle this line.
        if ($holdKey) {
            # A bare identifier with no brace after it is noise (SiiNunit, dlc markers).
            if ($null -ne $holdValue -and $d -le $Depth -and (-not $Pattern -or $holdKey -notmatch $Pattern)) {
                $out.Add($pad + $holdKey + ': ' + (Get-ValueHint -Value $holdValue -ShowValues $ShowValues))
            }
            $holdKey = $null
            $holdValue = $null
        }

        if ($t -eq '{') {
            $stack.Push('(unnamed)')
            if ($d -gt $Depth) { continue }
            Add-Block ($pad + '(unnamed)')
            continue
        }

        if ($t.StartsWith('}')) {
            if ($stack.Count -gt 0) { $null = $stack.Pop() }
            continue
        }

        # Block with the brace inline: "key {", "key[] {" and "name: value {".
        if ($t -match '^([A-Za-z_][A-Za-z0-9_\.\-]*)(\[\])?\s*\{$') {
            $label = $Matches[1]
            if ($Matches[2] -eq '[]') { $label += '[]' }
            $stack.Push($label)
            if ($d -gt $Depth) { continue }
            if ($Pattern -and $label -notmatch $Pattern) { continue }
            Add-Block ($pad + $label)
            continue
        }

        if ($t -match '^([A-Za-z_][A-Za-z0-9_\.\-]*)\s*:\s*(\S+)\s*\{$') {
            $label = $Matches[1] + ': ' + $Matches[2]
            $stack.Push($label)
            if ($d -gt $Depth) { continue }
            if ($Pattern -and $Matches[1] -notmatch $Pattern) { continue }
            Add-Block ($pad + $label)
            continue
        }

        # A bare identifier, e.g. "player_profile" or "cities[]" whose brace is on the next line.
        if ($t -match '^([A-Za-z_][A-Za-z0-9_\.\-]*)(\[\])?$' -and $t -notmatch '^SiiNunit$') {
            $holdKey = $Matches[1]
            if ($Matches[2] -eq '[]') { $holdKey += '[]' }
            $holdValue = $null
            continue
        }

        # A plain "key: value" line. Held, because the next line decides whether it was a
        # declaration or a leaf.
        if ($t -match '^([A-Za-z_][A-Za-z0-9_\.\-]*)(\[\])?\s*:\s*(.*)$') {
            $holdKey = $Matches[1]
            if ($Matches[2] -eq '[]') { $holdKey += '[]' }
            $holdValue = $Matches[3]
            continue
        }
    }

    # A file can end with a held leaf that never got its successor line.
    if ($holdKey -and $null -ne $holdValue) {
        $pad = '  ' * $stack.Count
        $out.Add($pad + $holdKey + ': ' + (Get-ValueHint -Value $holdValue -ShowValues $ShowValues))
    }

    return $out
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "No such file or folder: $Path"
}

$target = Get-Item -LiteralPath $Path

# --- Archive: .scs / .zip -----------------------------------------------------------
if (-not $target.PSIsContainer -and @('.scs', '.zip') -contains $target.Extension.ToLowerInvariant()) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($target.FullName)
    try {
        $entries = @($archive.Entries | Where-Object {
            $_.FullName -match '\.(sii|sui)$' -and (-not $Filter -or $_.FullName -like "*$Filter*")
        })

        Write-Output ('# archive: ' + $target.Name)
        Write-Output ('# bytes : ' + $target.Length)
        Write-Output ('# matches: ' + $entries.Count)
        Write-Output ''

        if ($entries.Count -eq 0) {
            Write-Output '# Nothing matched.'
            Write-Output '# Try -Filter def, or drop -Filter. Road Trip car content may live in a'
            Write-Output '# DLC archive (dlc_*.scs) rather than in base.scs.'
            return
        }

        foreach ($entry in $entries) {
            $reader = New-Object System.IO.StreamReader($entry.Open())
            $body = $reader.ReadToEnd()
            $reader.Close()
            Write-Output ('##### ' + $entry.FullName + ' #####')
            Get-SiiLines -Text $body -Depth $MaxDepth -Pattern $FocusPattern -ShowValues ([bool]$Raw) | ForEach-Object { Write-Output $_ }
        }
    } finally {
        $archive.Dispose()
    }
    return
}

# --- Folder -------------------------------------------------------------------------
if ($target.PSIsContainer) {
    $files = @(Get-ChildItem -LiteralPath $target.FullName -Recurse -Include *.sii, *.sui -File)
    Write-Output ('# folder: ' + $target.FullName)
    Write-Output ('# files : ' + $files.Count)
    Write-Output ''
    if ($files.Count -eq 0) {
        Write-Output '# No .sii or .sui files under this folder.'
        return
    }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($target.FullName.Length).TrimStart('\')
        Write-Output ('##### ' + $relative + ' #####')
        Get-SiiLines -Text ([System.IO.File]::ReadAllText($file.FullName)) -Depth $MaxDepth -Pattern $FocusPattern -ShowValues ([bool]$Raw) | ForEach-Object { Write-Output $_ }
    }
    return
}

# --- Single file --------------------------------------------------------------------
Write-Output ('# file  : ' + $target.Name)
Write-Output ('# bytes : ' + $target.Length)
Write-Output ('# depth : ' + $MaxDepth + '   mode : ' + $(if ($Raw) { 'RAW values shown' } else { 'values elided' }))
Write-Output ('# filter: ' + $(if ($FocusPattern) { $FocusPattern } else { 'none' }))
Write-Output ''
$lines = Get-SiiLines -Text ([System.IO.File]::ReadAllText($target.FullName)) -Depth $MaxDepth -Pattern $FocusPattern -ShowValues ([bool]$Raw)
foreach ($line in $lines) { Write-Output $line }
Write-Output ''
Write-Output ('# ' + $lines.Count + ' lines of structure')
