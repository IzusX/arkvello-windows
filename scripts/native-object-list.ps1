$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'build\native-bridge'
$objects = Join-Path $repo 'CPP\7zip\UI\FileManager\x64'
# Explicit reuse boundary: never offer the linker FM/GUI window objects.
$roots = 'C', 'CPP\Common', 'CPP\Windows', 'CPP\7zip\Common',
    'CPP\7zip\UI\Common', 'CPP\7zip\UI\Agent'
$names = foreach ($root in $roots) {
    Get-ChildItem -LiteralPath (Join-Path $repo $root) -File |
        Where-Object Extension -In '.c', '.cpp' | ForEach-Object BaseName
}
$names += 'CopyCoder', 'ItemNameUtils', 'StdAfx'
$lines = @('"' + (Join-Path $output 'Agent.obj') + '"')
foreach ($name in ($names | Sort-Object -Unique)) {
    if ($name -eq 'Agent') { continue }
    $path = Join-Path $objects ($name + '.obj')
    if (Test-Path -LiteralPath $path) { $lines += '"' + $path + '"' }
}
$lines | Set-Content -LiteralPath (Join-Path $output 'upstream-objects.rsp') -Encoding ascii
