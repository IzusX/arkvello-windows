#requires -Version 7.0
param([string]$Executable = "$PSScriptRoot/../build/portable-x64/Arkvello.exe")
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo ('build/stage4-client-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$start = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Executable))
$start.WorkingDirectory = $repo
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.ArgumentList.Add('--verify-stage4')
$start.ArgumentList.Add($output)
$start.Environment['DOTNET_ROOT'] = Join-Path $output 'no-global-runtime'
$start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$process = [Diagnostics.Process]::Start($start)
try {
    if (!$process.WaitForExit(45000)) { throw "Client checks timed out. Evidence: $output" }
    if ($process.ExitCode -ne 0) { throw "Client checks failed with exit $($process.ExitCode). Evidence: $output" }
    $evidence = Join-Path $output 'client-checks.json'
    $checks = @(Get-Content -LiteralPath $evidence -Raw | ConvertFrom-Json)
    if ($checks.Count -lt 20 -or @($checks | Where-Object { !$_.passed }).Count) { throw "Incomplete or failed client evidence: $evidence" }
    $checks | ForEach-Object { 'PASS ' + $_.name }
    Write-Output $evidence
}
finally { $process.Dispose() }
