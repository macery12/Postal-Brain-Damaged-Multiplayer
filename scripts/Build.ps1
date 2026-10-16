[CmdletBinding()]
param([string]$GameDir = (Join-Path $PSScriptRoot '..\..\Game'), [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_HOME = Join-Path $repo '.local\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_PACKAGES = Join-Path $repo '.local\nuget'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
Push-Location $repo
try {
    dotnet restore src/PBDMP.Plugin/PBDMP.Plugin.csproj --configfile NuGet.Config "-p:GameDir=$GameDir" --nologo
    if ($LASTEXITCODE) { throw 'Plugin restore failed.' }
    dotnet build src/PBDMP.Plugin/PBDMP.Plugin.csproj -c Release --no-restore "-p:GameDir=$GameDir" --nologo
    if ($LASTEXITCODE) { throw 'Plugin build failed.' }
    if (!$SkipTests) {
        dotnet restore tests/PBDMP.Network.Tests/PBDMP.Network.Tests.csproj --configfile NuGet.Config --nologo
        if ($LASTEXITCODE) { throw 'Test restore failed.' }
        dotnet run --project tests/PBDMP.Network.Tests/PBDMP.Network.Tests.csproj -c Release --no-restore
        if ($LASTEXITCODE) { throw 'Network acceptance tests failed.' }
        dotnet restore tests/PBDMP.Reporting.Tests/PBDMP.Reporting.Tests.csproj --configfile NuGet.Config --nologo
        if ($LASTEXITCODE) { throw 'Reporting test restore failed.' }
        dotnet run --project tests/PBDMP.Reporting.Tests/PBDMP.Reporting.Tests.csproj -c Release --no-restore
        if ($LASTEXITCODE) { throw 'Reporting tests failed.' }
    }
    $package = Join-Path $repo 'artifacts\PBDMP\BepInEx\plugins\PBDMP'
    New-Item -ItemType Directory -Force -Path $package | Out-Null
    Copy-Item -LiteralPath 'src/PBDMP.Plugin/bin/Release/net6.0/PBDMP.dll' -Destination $package -Force
    Copy-Item -LiteralPath 'src/PBDMP.Plugin/bin/Release/net6.0/PBDMP.Network.dll' -Destination $package -Force
    Write-Host "Build output: $package"
} finally { Pop-Location }
