param([string]$RunDirectory)

$ErrorActionPreference = 'Stop'

$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runId = [Guid]::NewGuid().ToString('N')
$version = "0.1.0-smoke.$runId"

if ([string]::IsNullOrWhiteSpace($RunDirectory)) {
    $RunDirectory = Join-Path $repository "artifacts/package-tests/$runId"
}

$runDirectory = [System.IO.Path]::GetFullPath($RunDirectory)
$feed = Join-Path $runDirectory 'feed'
$packages = Join-Path $runDirectory 'packages'
$consumer = Join-Path $runDirectory 'consumer'
New-Item -ItemType Directory -Path $feed, $consumer -Force | Out-Null

dotnet pack (Join-Path $repository 'src/TinyBus/TinyBus.csproj') `
    -c Release `
    -o $feed `
    "-p:PackageVersion=$version" `
    -warnaserror

if ($LASTEXITCODE -ne 0) {
    throw 'Package creation failed.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = Join-Path $feed "TinySuite.TinyBus.$version.nupkg"
$archive = [System.IO.Compression.ZipFile]::OpenRead($package)

try {
    foreach ($expected in @(
        'lib/net8.0/TinyBus.dll',
        'analyzers/dotnet/cs/TinyBus.SourceGen.dll',
        'README.md')) {
        if ($null -eq $archive.GetEntry($expected)) {
            throw "Missing package asset: $expected"
        }
    }

    $manifestEntry = $archive.GetEntry('TinySuite.TinyBus.nuspec')
    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try {
        [xml]$manifest = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $dependencies = @($manifest.SelectNodes("//*[local-name()='dependency']"))
    if ($dependencies.Count -ne 0) {
        throw 'TinyBus core package must not have runtime package dependencies.'
    }
}
finally {
    $archive.Dispose()
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Consumer') -Destination $consumer -Recurse
$consumerProject = Join-Path $consumer 'Consumer/Consumer.csproj'
$projectText = [System.IO.File]::ReadAllText($consumerProject)
$projectText = $projectText.Replace('Version="0.1.0-dev"', "Version=`"$version`"")
[System.IO.File]::WriteAllText($consumerProject, $projectText)

$configuration = Join-Path $runDirectory 'NuGet.Config'
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
@"
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local"><package pattern="TinySuite.TinyBus" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $configuration -Encoding UTF8

$properties = @("-p:RestorePackagesPath=$packages")
dotnet restore $consumerProject --configfile $configuration @properties -warnaserror
if ($LASTEXITCODE -ne 0) {
    throw 'Consumer restore failed.'
}

dotnet build $consumerProject -c Release --no-restore @properties -warnaserror
if ($LASTEXITCODE -ne 0) {
    throw 'Consumer build failed.'
}

dotnet (Join-Path $consumer 'Consumer/bin/Release/net8.0/Consumer.dll')
if ($LASTEXITCODE -ne 0) {
    throw 'Consumer behavior failed.'
}

$diagnostics = & dotnet build $consumerProject `
    -c Release `
    --no-restore `
    @properties `
    -p:IncludeInvalidHandler=true `
    -warnaserror 2>&1
$diagnosticExitCode = $LASTEXITCODE
$diagnostics | Set-Content -LiteralPath (Join-Path $runDirectory 'invalid-build.log')

if ($diagnosticExitCode -eq 0 -or ($diagnostics -join "`n") -notmatch 'error TBUS003:') {
    throw "Expected the packaged generator to report TBUS003.`n$diagnostics"
}

Write-Host "TinyBus package verification passed. Artifacts: $runDirectory"
exit 0
