#requires -Version 7.0
param([string]$Executable, [string]$Fixture)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (!$Executable) { $Executable = Join-Path $repo 'build\portable-x64\Arkvello.exe' }
if (!$Fixture) { $Fixture = Join-Path $repo 'build\stage3-fixture' }
$evidence = Join-Path $Fixture 'evidence\native-layout.json'
$started = [DateTime]::UtcNow
$launch = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Executable))
$launch.WorkingDirectory = $repo
$launch.UseShellExecute = $false
$archive = Join-Path $Fixture 'encoded launch Ω.7z'
Copy-Item -LiteralPath (Join-Path $Fixture 'encoded.7z') -Destination $archive -Force
foreach ($argument in $archive, '--verify-stage3', $Fixture, '--diagnostics') { $launch.ArgumentList.Add($argument) }
# An app-local CLR must be selected even with no valid global runtime root.
# This does not uninstall installed runtimes or claim a clean VM.
$launch.Environment['DOTNET_ROOT'] = Join-Path $repo 'build\no-installed-runtime'
$launch.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$process = [Diagnostics.Process]::Start($launch)
try {
    if (!$process.WaitForExit(45000)) { throw "Client diagnostics did not finish within 45 seconds (PID $($process.Id))." }
    if ($process.ExitCode -ne 0) { throw "Client diagnostics exited with $($process.ExitCode). Evidence: $evidence" }
}
finally { $process.Dispose() }
if (!(Test-Path -LiteralPath $evidence) -or (Get-Item -LiteralPath $evidence).LastWriteTimeUtc -lt $started) { throw 'Fresh client evidence was not written.' }
$checks = @(Get-Content -LiteralPath $evidence -Raw | ConvertFrom-Json)
if (@($checks | Where-Object passed -NE $true).Count) { throw "Client checks failed: $evidence" }
Write-Output "$($checks.Count) client checks passed: $evidence"
