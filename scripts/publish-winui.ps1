#requires -Version 7.0
param([switch]$SkipNative)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $env:DOTNET_CLI_HOME = Join-Path $repo 'build\dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $repo 'build\nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    if (!$SkipNative) {
        & ./scripts/build-native-bridge.cmd
        if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
    }
    $project = 'src/Arkvello.WinUI/Arkvello.WinUI.csproj'
    & dotnet restore $project --locked-mode --source https://api.nuget.org/v3/index.json -p:SelfContained=true
    if ($LASTEXITCODE -ne 0) { throw 'Portable dependency restore failed.' }
    $output = Join-Path $repo 'build\portable-x64'
    # WinUI generates app PRI/XBF resources on Build. The .NET publish file list
    # alone omits these generated app resources in this unpackaged project.
    & dotnet build $project --no-restore -c Release -p:Platform=x64 -p:SelfContained=true
    if ($LASTEXITCODE -ne 0) { throw 'Portable client build failed.' }
    & dotnet publish $project --no-build --no-restore -c Release -p:Platform=x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
    $built = Join-Path $repo 'src\Arkvello.WinUI\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64'
    foreach ($resource in @((Get-Item -LiteralPath (Join-Path $built 'Arkvello.pri'))) + @(Get-ChildItem -LiteralPath $built -Filter '*.xbf' -Recurse -File)) {
        $destination = Join-Path $output ([IO.Path]::GetRelativePath($built, $resource.FullName))
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $resource.FullName -Destination $destination -Force
    }
    $runtime = (Get-Content (Join-Path $output 'Arkvello.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks | Where-Object name -EQ 'Microsoft.NETCore.App'
    $runtimeNotices = Join-Path $output 'Licenses/DotNET'
    New-Item -ItemType Directory -Path $runtimeNotices -Force | Out-Null
    foreach ($name in 'LICENSE.TXT','THIRD-PARTY-NOTICES.TXT') {
        Copy-Item -LiteralPath (Join-Path $env:NUGET_PACKAGES ('microsoft.netcore.app.runtime.win-x64/' + $runtime.version + '/' + $name)) -Destination $runtimeNotices -Force
    }
    $lock = Get-Content src/Arkvello.WinUI/packages.lock.json -Raw | ConvertFrom-Json
    foreach ($target in $lock.dependencies.PSObject.Properties) {
        foreach ($package in $target.Value.PSObject.Properties) {
            $source = Join-Path $env:NUGET_PACKAGES ($package.Name.ToLowerInvariant() + '/' + $package.Value.resolved)
            $notices = @(Get-ChildItem -LiteralPath $source -File -ErrorAction SilentlyContinue | Where-Object Name -Match '(?i)license|notice')
            if (!$notices.Count) { continue }
            $destination = Join-Path $output ('Licenses/NuGet/' + $package.Name + '.' + $package.Value.resolved)
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $destination -Force }
        }
    }
    foreach ($file in 'Arkvello.exe','Arkvello.dll','Arkvello.Native.dll','7z.dll','7z.sfx','coreclr.dll','hostfxr.dll','Microsoft.UI.Xaml.dll','Arkvello.pri','App.xbf','MainWindow.xbf','Assets/Fluent/Icons.xbf','License.txt','Licenses/DotNET/THIRD-PARTY-NOTICES.TXT') {
        if (!(Test-Path -LiteralPath (Join-Path $output $file))) { throw "Portable payload incomplete: $file" }
    }
    $manifestPath = Join-Path $output 'payload-manifest.json'
    $manifest = Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object FullName -NE $manifestPath | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($output, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes = $_.Length }
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Output "Portable development build: $output\Arkvello.exe"
}
finally { Pop-Location }
