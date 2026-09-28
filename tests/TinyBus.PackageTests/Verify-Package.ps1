param(
    [string]$PackageVersion = "",
    [string]$RunDirectory = ""
)

$ErrorActionPreference = 'Stop'

function Invoke-Native {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Read-PackageManifest {
    param($Archive)

    $manifestEntry = $Archive.Entries | Where-Object {
        $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase)
    }
    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())

    try {
        [xml]$manifest = $reader.ReadToEnd()
        return $manifest.package.metadata
    }
    finally {
        $reader.Dispose()
    }
}

function Pack-ReleaseTrain {
    param(
        [string]$Repository,
        [string]$Feed,
        [string]$Version
    )

    $projects = @(
        'src/TinyBus/TinyBus.csproj',
        'src/TinyBus.PostgreSql/TinyBus.PostgreSql.csproj',
        'src/TinyBus.RabbitMq/TinyBus.RabbitMq.csproj'
    )

    Invoke-Native 'dotnet' @(
        'build',
        (Join-Path $Repository 'TinyBus.slnx'),
        '-c', 'Release',
        '-warnaserror')

    foreach ($project in $projects) {
        $projectPath = Join-Path $Repository $project
        Invoke-Native 'dotnet' @(
            'pack', $projectPath,
            '-c', 'Release',
            '--no-build',
            '-o', $Feed,
            "/p:PackageVersion=$Version",
            "/p:Version=$Version",
            '-warnaserror')
    }
}

function Test-PackageManifest {
    param(
        $Metadata,
        [string]$PackageId,
        [string]$Version
    )

    $expectedValues = [ordered]@{
        id = $PackageId
        version = $Version
        authors = 'Jorge Durban Antunano'
        projectUrl = 'https://github.com/george2006/TinyBus'
        readme = 'README.md'
    }

    foreach ($expected in $expectedValues.GetEnumerator()) {
        $actual = [string]$Metadata.($expected.Key)
        if ($actual -ne $expected.Value) {
            throw "Package $PackageId has invalid $($expected.Key): $actual"
        }
    }

    $licenseType = [string]$Metadata.license.type
    $licenseName = [string]$Metadata.license.'#text'
    $usesApacheLicense = $licenseType -eq 'expression' -and $licenseName -eq 'Apache-2.0'

    if (-not $usesApacheLicense) {
        throw "Package $PackageId must use the Apache-2.0 license expression."
    }

    $repositoryUrl = [string]$Metadata.repository.url
    $repositoryType = [string]$Metadata.repository.type
    $repositoryUrlMatches = $repositoryUrl -eq 'https://github.com/george2006/TinyBus'
    $repositoryTypeMatches = $repositoryType -eq 'git'
    $hasExpectedRepository = $repositoryUrlMatches -and $repositoryTypeMatches

    if (-not $hasExpectedRepository) {
        throw "Package $PackageId has invalid repository metadata."
    }

    $description = [string]$Metadata.description
    $tags = [string]$Metadata.tags
    $hasDescription = -not [string]::IsNullOrWhiteSpace($description)
    $hasTags = -not [string]::IsNullOrWhiteSpace($tags)

    if (-not $hasDescription -or -not $hasTags) {
        throw "Package $PackageId requires a description and tags."
    }
}

function Test-PackageDependencies {
    param(
        $Metadata,
        [string]$PackageId,
        [string[]]$ExpectedDependencies
    )

    $dependencies = @($Metadata.dependencies.group.dependency)
    $actualDependencies = @($dependencies | ForEach-Object {
        "$($_.id)=$($_.version)"
    })
    $difference = Compare-Object $ExpectedDependencies $actualDependencies

    if ($difference) {
        $actual = $actualDependencies -join ', '
        throw "Package $PackageId has unexpected dependencies: $actual"
    }
}

function Test-PackageLegalFiles {
    param(
        $Archive,
        [string]$PackageId
    )

    $expectedCopyright = 'Copyright 2026 Jorge Durban Antunano'
    $legalFiles = @('LICENSE', 'NOTICE')

    foreach ($legalFile in $legalFiles) {
        $entry = $Archive.GetEntry($legalFile)

        if ($null -eq $entry) {
            throw "Package $PackageId does not contain $legalFile."
        }

        $reader = [System.IO.StreamReader]::new($entry.Open())

        try {
            $content = $reader.ReadToEnd()

            if (-not $content.Contains($expectedCopyright)) {
                throw "Package $PackageId does not declare its copyright in $legalFile."
            }
        }
        finally {
            $reader.Dispose()
        }
    }
}

function Test-ReleaseTrainPackages {
    param(
        [string]$Feed,
        [string]$Version
    )

    $packages = [ordered]@{
        'TinySuite.TinyBus' = @(
            'Microsoft.Extensions.DependencyInjection.Abstractions=9.0.10',
            'Microsoft.Extensions.Hosting.Abstractions=9.0.10')
        'TinySuite.TinyBus.PostgreSql' = @(
            "TinySuite.TinyBus=$Version",
            'Npgsql=8.0.9')
        'TinySuite.TinyBus.RabbitMq' = @(
            "TinySuite.TinyBus=$Version",
            'RabbitMQ.Client=7.2.2')
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    foreach ($package in $packages.GetEnumerator()) {
        $packageId = $package.Key
        $packagePath = Join-Path $Feed "$packageId.$Version.nupkg"

        if (-not (Test-Path -LiteralPath $packagePath)) {
            throw "Expected package was not produced: $packagePath"
        }

        $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)

        try {
            $metadata = Read-PackageManifest $archive
            Test-PackageManifest $metadata $packageId $Version
            Test-PackageDependencies $metadata $packageId $package.Value
            Test-PackageLegalFiles $archive $packageId

            if ($null -eq $archive.GetEntry('README.md')) {
                throw "Package $packageId does not contain its declared README."
            }

            $assemblyName = $packageId.Replace('TinySuite.', '')
            $assemblyPath = "lib/net8.0/$assemblyName.dll"
            if ($null -eq $archive.GetEntry($assemblyPath)) {
                throw "Package $packageId does not contain $assemblyPath."
            }

            $isCorePackage = $packageId -eq 'TinySuite.TinyBus'
            $generator = $archive.GetEntry('analyzers/dotnet/cs/TinyBus.SourceGen.dll')
            $generatorIsMissing = $null -eq $generator

            if ($isCorePackage -and $generatorIsMissing) {
                throw 'The Core package does not contain its source generator.'
            }
        }
        finally {
            $archive.Dispose()
        }
    }

    Write-Host 'TinyBus release-train package manifests and contents passed.'
}

function Write-NuGetConfiguration {
    param(
        [string]$Path,
        [string]$Feed
    )

    $escapedFeed = [System.Security.SecurityElement]::Escape($Feed)
    @"
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local"><package pattern="TinySuite.TinyBus*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Copy-Consumer {
    param(
        [string]$Name,
        [string]$Destination,
        [string]$Version
    )

    $source = Join-Path $PSScriptRoot $Name
    $target = Join-Path $Destination $Name
    Copy-Item -LiteralPath $source -Destination $target -Recurse

    $project = Join-Path $target "$Name.csproj"
    $projectText = [System.IO.File]::ReadAllText($project)
    $projectText = $projectText.Replace('Version="0.1.0-dev"', "Version=`"$Version`"")
    [System.IO.File]::WriteAllText($project, $projectText)

    return $project
}

function Test-CoreConsumer {
    param(
        [string]$Project,
        [string]$NuGetConfiguration,
        [string]$Packages,
        [string]$RunDirectory
    )

    $properties = @("-p:RestorePackagesPath=$Packages")
    $restoreArguments = @(
        'restore', $Project,
        '--configfile', $NuGetConfiguration,
        '--no-cache', '--force') + $properties
    Invoke-Native 'dotnet' $restoreArguments

    $buildArguments = @(
        'build', $Project,
        '-c', 'Release',
        '--no-restore',
        '-warnaserror') + $properties
    Invoke-Native 'dotnet' $buildArguments

    $output = Join-Path (Split-Path $Project) 'bin/Release/net8.0/Consumer.dll'
    Invoke-Native 'dotnet' @($output)

    $diagnostics = & dotnet build $Project `
        -c Release `
        --no-restore `
        @properties `
        -p:IncludeInvalidHandler=true `
        -warnaserror 2>&1
    $diagnosticExitCode = $LASTEXITCODE
    $diagnostics | Set-Content -LiteralPath (Join-Path $RunDirectory 'invalid-build.log')

    if ($diagnosticExitCode -eq 0 -or ($diagnostics -join "`n") -notmatch 'error TBUS003:') {
        throw "Expected the packaged generator to report TBUS003.`n$diagnostics"
    }
}

function Test-RuntimeConsumer {
    param(
        [string]$Project,
        [string]$NuGetConfiguration,
        [string]$Packages
    )

    $properties = @("-p:RestorePackagesPath=$Packages")
    $restoreArguments = @(
        'restore', $Project,
        '--configfile', $NuGetConfiguration,
        '--no-cache', '--force') + $properties
    Invoke-Native 'dotnet' $restoreArguments

    $buildArguments = @(
        'build', $Project,
        '-c', 'Release',
        '--no-restore',
        '-warnaserror') + $properties
    Invoke-Native 'dotnet' $buildArguments

    $output = Join-Path (Split-Path $Project) 'bin/Release/net8.0/RuntimeConsumer.dll'
    Invoke-Native 'dotnet' @($output)
}

$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runId = [Guid]::NewGuid().ToString('N')

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion = "0.1.0-smoke.$runId"
}

if ([string]::IsNullOrWhiteSpace($RunDirectory)) {
    $RunDirectory = Join-Path $repository "artifacts/package-tests/$runId"
}

$runDirectory = [System.IO.Path]::GetFullPath($RunDirectory)
$feed = Join-Path $runDirectory 'feed'
$packages = Join-Path $runDirectory 'packages'
$consumers = Join-Path $runDirectory 'consumers'
New-Item -ItemType Directory -Path $feed, $consumers -Force | Out-Null

Pack-ReleaseTrain $repository $feed $PackageVersion
Test-ReleaseTrainPackages $feed $PackageVersion

$configuration = Join-Path $runDirectory 'NuGet.Config'
Write-NuGetConfiguration $configuration $feed

$coreConsumer = Copy-Consumer 'Consumer' $consumers $PackageVersion
Test-CoreConsumer $coreConsumer $configuration $packages $runDirectory

& (Join-Path $PSScriptRoot 'Verify-Topology.ps1') `
    -PackageVersion $PackageVersion `
    -RunDirectory $runDirectory

$runtimeConsumer = Copy-Consumer 'RuntimeConsumer' $consumers $PackageVersion
Test-RuntimeConsumer $runtimeConsumer $configuration $packages

Write-Host "TinyBus package verification passed. Artifacts: $runDirectory"
