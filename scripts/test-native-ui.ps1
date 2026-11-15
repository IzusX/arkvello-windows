param(
    [string]$BinaryDirectory = (Join-Path $PSScriptRoot '..\build\windows-x64'),
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot ('..\build\native-ui-' + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$binaries = (Resolve-Path -LiteralPath $BinaryDirectory).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$test = Join-Path $repo 'build\test-native-ui.exe'
if (!(Test-Path -LiteralPath $test)) {
    throw 'Build the integration test first: scripts\build-native-ui-test.cmd'
}
& $test $binaries $evidence
if ($LASTEXITCODE -ne 0) { throw 'Native GUI integration checks failed.' }

$cli = Join-Path $binaries '7z.exe'
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $evidence 'fixture\source.txt') -Algorithm SHA256).Hash
foreach ($format in @('7z', 'zip')) {
    $archiveName = if ($format -eq '7z') { 'native-encrypted.7z' } else { 'native.zip' }
    $archive = Join-Path $evidence $archiveName
    $passwordArgs = @(if ($format -eq '7z') { '-parkvello-fixture' })
    & $cli t $archive @passwordArgs -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Native $format archive test failed." }
    $extracted = Join-Path $evidence ('extracted-' + $format)
    & $cli x $archive @passwordArgs ('-o' + $extracted) -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Native $format archive extraction failed." }
    $hash = (Get-FileHash -LiteralPath (Join-Path $extracted 'source.txt') -Algorithm SHA256).Hash
    if ($hash -ne $sourceHash) { throw "Native $format archive round-trip changed file contents." }
    Write-Host "PASS native $format archive extraction and SHA256 round-trip"
}
$previousErrorAction = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue' # Windows PowerShell wraps native stderr as error records.
    & $cli t (Join-Path $evidence 'native-encrypted.7z') -pincorrect-fixture-password -y *> (Join-Path $evidence 'wrong-password.log')
    $wrongPasswordExit = $LASTEXITCODE
} finally { $ErrorActionPreference = $previousErrorAction }
if ($wrongPasswordExit -eq 0) { throw 'Encrypted headers unexpectedly opened with the wrong password.' }
Write-Host 'PASS encrypted archive rejects wrong password'
Write-Host "Native UI evidence: $evidence"
# The expected wrong-password failure must not become the shell/CI exit code.
$global:LASTEXITCODE = 0
