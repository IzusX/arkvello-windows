#requires -Version 7.0
param([string]$Executable = "$PSScriptRoot/../build/portable-x64/Arkvello.exe")
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo ('build/window-preferences-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $output 'files'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$settings = Join-Path $output 'window.json'
$checks = foreach ($mode in 'write','read','corrupt') {
    if ($mode -eq 'corrupt') { Set-Content -LiteralPath $settings -Value '{truncated' -NoNewline }
    $start = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Executable))
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    foreach ($argument in '--open',$fixture,'--verify-preferences',$output,$mode) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(15000)) { throw "Client startup or shutdown timed out. Evidence: $output" }
        if ($process.ExitCode -ne 0) { throw "Preferences check $mode failed. Evidence: $output" }
        Get-Content -LiteralPath (Join-Path $output "preferences-$mode.json") -Raw | ConvertFrom-Json
    }
    finally { $process.Dispose() }
    if ($mode -eq 'write') {
        $saved = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
        [ordered]@{ name = 'closing a changed window writes preferences without a temporary file'; passed = $saved.TwoPanels -and $saved.Theme -eq 'Dark' -and !@(Get-ChildItem -LiteralPath $output -Filter '*.tmp').Count }
        $savedHash = (Get-FileHash -LiteralPath $settings).Hash
    }
    if ($mode -eq 'read') {
        [ordered]@{ name = 'an unchanged window leaves saved preferences intact'; passed = (Get-FileHash -LiteralPath $settings).Hash -eq $savedHash }
    }
    if ($mode -eq 'corrupt') {
        [ordered]@{ name = 'a truncated file does not block startup or get overwritten by an unchanged window'; passed = (Get-Content -LiteralPath $settings -Raw) -eq '{truncated' }
    }
}
$evidence = Join-Path $output 'preferences-checks.json'
$checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $evidence -Encoding utf8
if (@($checks | Where-Object { !$_.passed }).Count) { throw "Preferences check failed. Evidence: $evidence" }
$checks | ForEach-Object { 'PASS ' + $_.name }
Write-Output $evidence
