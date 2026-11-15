[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $repo 'build\windows-x64'
$sevenZip = Join-Path $bin '7z.exe'
foreach ($file in '7z.exe', '7z.dll', '7zG.exe', '7zFM.exe') {
    if (-not (Test-Path (Join-Path $bin $file))) { throw "Missing binary: $file" }
}

function Invoke-SevenZip {
    param([string[]]$Arguments)
    & $sevenZip @Arguments
    if ($LASTEXITCODE -ne 0) { throw "7z failed with exit code $LASTEXITCODE" }
}

$work = Join-Path $repo ('build\smoke-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'input'
New-Item -ItemType Directory -Path $source -Force | Out-Null
# Construct Unicode names without depending on the script's source encoding in Windows PowerShell.
$name = (-join (0x0442, 0x0435, 0x0441, 0x0442 | ForEach-Object { [char]$_ })) + ' file.txt'
$inputFile = Join-Path $source $name
[IO.File]::WriteAllText($inputFile, "Arkvello baseline archive test`r`nUnicode: $name")
$expected = (Get-FileHash $inputFile -Algorithm SHA256).Hash

Push-Location $source
try {
    foreach ($case in @(
        @{ Name = 'plain.7z'; Options = @('-t7z'); Password = @() },
        @{ Name = 'plain.zip'; Options = @('-tzip'); Password = @() },
        @{ Name = 'encrypted.7z'; Options = @('-t7z', '-pbaseline-test', '-mhe=on'); Password = @('-pbaseline-test') }
    )) {
        $archive = Join-Path $work $case.Name
        $extract = Join-Path $work ($case.Name + '-extracted')
        Invoke-SevenZip -Arguments (@('a', $archive, $name) + $case.Options)
        Invoke-SevenZip -Arguments (@('t', $archive) + $case.Password)
        Invoke-SevenZip -Arguments (@('x', $archive, "-o$extract", '-y') + $case.Password)
        $actual = (Get-FileHash (Join-Path $extract $name) -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw "Round-trip mismatch: $($case.Name)" }
    }
} finally {
    Pop-Location
}
Write-Host "Smoke tests passed (7z, ZIP, encrypted 7z, Unicode and SHA-256 round-trip). Results: $work"
