param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'build\native-bridge' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$objects = Join-Path $output 'obj'
New-Item -ItemType Directory -Path $objects -Force | Out-Null

# Compile the entire explicit closure on every invocation. This small target
# deliberately avoids stale objects after any upstream header/define change.
$manifest = Join-Path $repo 'src\Arkvello.Native\sources.txt'
$sources = @(Get-Content -LiteralPath $manifest | Where-Object { $_ -and !$_.StartsWith('#') })
$sources += 'src/Arkvello.Native/bridge.cpp'
$names = @{}
foreach ($source in $sources) {
    if (!(Test-Path -LiteralPath (Join-Path $repo $source) -PathType Leaf)) { throw "Missing source: $source" }
    $name = [IO.Path]::GetFileNameWithoutExtension($source)
    if ($names.ContainsKey($name)) { throw "Duplicate object basename: $name" }
    $names[$name] = $source
}

# One set of ABI/layout flags for every C/C++ translation unit. In particular,
# Z7_DEVICE_FILE changes upstream class layouts used across this boundary.
$flags = @('/nologo', '/c', '/W4', '/WX', '/O2', '/Gy', '/MT', '/MP8',
    '/DUNICODE', '/D_UNICODE', '/DWIN32', '/D_WIN64', '/D_WIN32_WINNT=0x0A00',
    '/DZ7_EXTERNAL_CODECS', '/DZ7_DEVICE_FILE', '/DZ7_LANG', '/DARKVELLO_NATIVE', '/DARKVELLO_FS_SERVICE')
Push-Location $objects
try {
    foreach ($extension in '.c', '.cpp') {
        $arguments = @($flags)
        if ($extension -eq '.cpp') { $arguments += '/std:c++17', '/EHsc', '/GR-' }
        $arguments += $sources | Where-Object { [IO.Path]::GetExtension($_) -eq $extension } |
            ForEach-Object { '"' + (Join-Path $repo $_) + '"' }
        $response = Join-Path $output ('compile' + $extension + '.rsp')
        $arguments | Set-Content -LiteralPath $response -Encoding unicode
        & cl.exe "@$response"
        if ($LASTEXITCODE -ne 0) { throw "Native $extension compilation failed." }
    }
    # Explicit object list: stale files in obj/ can never join the new DLL.
    $link = @('/NOLOGO', '/DLL', '/OPT:REF', '/OPT:ICF',
        ('/OUT:"' + (Join-Path $output 'Arkvello.Native.dll') + '"'),
        ('/IMPLIB:"' + (Join-Path $output 'Arkvello.Native.lib') + '"'),
        ('/MAP:"' + (Join-Path $output 'Arkvello.Native.map') + '"'))
    $link += $sources | ForEach-Object { '"' + [IO.Path]::GetFileNameWithoutExtension($_) + '.obj"' }
    $link += 'oleaut32.lib', 'ole32.lib', 'advapi32.lib', 'user32.lib', 'shell32.lib'
    $response = Join-Path $output 'link.rsp'
    $link | Set-Content -LiteralPath $response -Encoding unicode
    & link.exe "@$response"
    if ($LASTEXITCODE -ne 0) { throw 'Native bridge link failed.' }
    Write-Output "Native bridge: $output\Arkvello.Native.dll ($($sources.Count) source files)"
}
finally { Pop-Location }
