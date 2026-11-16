param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $env:DOTNET_CLI_HOME = Join-Path $repo 'build\dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $repo 'build\nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    & (Join-Path $PSScriptRoot 'build-native-bridge.cmd')
    if ($LASTEXITCODE -ne 0) { throw 'Native bridge build failed.' }
    & dotnet restore src\Arkvello.WinUI\Arkvello.WinUI.csproj --locked-mode --source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw 'WinUI dependency restore failed.' }
    & dotnet build src\Arkvello.WinUI\Arkvello.WinUI.csproj --no-restore -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'WinUI build failed.' }
    Write-Output "WinUI executable: $repo\src\Arkvello.WinUI\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\Arkvello.exe"
}
finally { Pop-Location }
