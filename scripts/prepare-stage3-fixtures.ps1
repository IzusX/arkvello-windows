$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repo 'build\stage3-fixture'
$files = Join-Path $fixture 'files'
New-Item -ItemType Directory -Path $files -Force | Out-Null
foreach ($index in 0..9999) {
    $name = 'file-{0:D5}-Unicode-Ω-длинное-имя.txt' -f $index
    [IO.File]::WriteAllText((Join-Path $files $name), ('Arkvello real I/O fixture ' + $index + ' ' + ('data ' * 200)))
}
Push-Location $files
try {
    & (Join-Path $repo 'build\windows-x64\7z.exe') a (Join-Path $fixture 'encoded.7z') . -mx=1 -m0=LZMA2 -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Encoded archive fixture failed.' }
    & (Join-Path $repo 'build\windows-x64\7z.exe') a (Join-Path $fixture 'encrypted.7z') 'file-00000-*' -mx=0 -pStage3FixtureOnly -mhe=on -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Encrypted archive fixture failed.' }
}
finally { Pop-Location }
Write-Output $fixture
