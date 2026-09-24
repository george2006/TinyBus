param(
    [Parameter(Mandatory = $true)][string]$PackageVersion,
    [Parameter(Mandatory = $true)][string]$RunDirectory
)

$ErrorActionPreference = 'Stop'

function Invoke-TopologyVerification {
    $topologyDirectory = Join-Path $RunDirectory 'topology'
    Copy-PackagedSamples $topologyDirectory

    $hostDirectory = Join-Path $topologyDirectory 'TinyBus.Sample.Host'
    $hostProject = Join-Path $hostDirectory 'TinyBus.Sample.Host.csproj'
    $properties = @("-p:RestorePackagesPath=$(Join-Path $RunDirectory 'packages')")

    dotnet restore $hostProject --configfile (Join-Path $RunDirectory 'NuGet.Config') @properties -warnaserror
    if ($LASTEXITCODE -ne 0) {
        throw 'Packaged topology restore failed.'
    }

    dotnet build $hostProject -c Release --no-restore @properties -warnaserror
    if ($LASTEXITCODE -ne 0) {
        throw 'Packaged topology build failed.'
    }

    Test-TopologyOutput $hostDirectory
    Test-CompilerAssetIsolation $topologyDirectory

    # Invalid topology emits no manifest; isolate its diagnostics from missing-generated-API errors.
    Set-Content -LiteralPath (Join-Path $hostDirectory 'Program.cs') -Value 'return;'

    Test-TopologyDiagnostic $hostProject $properties 'DuplicateCommand' 'TBUS003'
    Test-TopologyDiagnostic $hostProject $properties 'DuplicateRequest' 'TBUS004'
    Test-TopologyDiagnostic $hostProject $properties 'ConflictingSemantics' 'TBUS005'

    Write-Host 'TinyBus packaged multi-assembly topology verification passed.'
}

function Copy-PackagedSamples([string]$destination) {
    $sampleRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../samples'))

    foreach ($name in @('Contracts', 'Orders', 'Payments', 'Host')) {
        $projectName = "TinyBus.Sample.$name"
        $sourceDirectory = Join-Path $sampleRoot $projectName
        $targetDirectory = Join-Path $destination $projectName
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

        Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' -File |
            Copy-Item -Destination $targetDirectory

        [xml]$project = Get-Content -LiteralPath (Join-Path $sourceDirectory "$projectName.csproj") -Raw
        $sourceReferences = @($project.SelectNodes("//ProjectReference[starts-with(@Include, '../../src/')]"))
        foreach ($reference in $sourceReferences) {
            $reference.ParentNode.RemoveChild($reference) | Out-Null
        }

        $packageGroup = $project.CreateElement('ItemGroup')
        $packageReference = $project.CreateElement('PackageReference')
        $packageReference.SetAttribute('Include', 'TinySuite.TinyBus')
        $packageReference.SetAttribute('Version', $PackageVersion)
        $packageGroup.AppendChild($packageReference) | Out-Null
        $project.DocumentElement.AppendChild($packageGroup) | Out-Null
        $project.Save((Join-Path $targetDirectory "$projectName.csproj"))
    }
}

function Test-TopologyOutput([string]$hostDirectory) {
    $hostAssembly = Join-Path $hostDirectory 'bin/Release/net8.0/TinyBus.Sample.Host.dll'
    $actual = & dotnet $hostAssembly 'commerce.packaged'
    if ($LASTEXITCODE -ne 0) {
        throw 'Packaged topology execution failed.'
    }

    $actual | Set-Content -LiteralPath (Join-Path $RunDirectory 'topology-output.log')
    $expected = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Topology/expected-output.txt')
    if (($actual -join "`n") -cne ($expected -join "`n")) {
        throw "Packaged sample differs from the expected topology and handler execution results.`n$($actual -join "`n")"
    }
}

function Test-CompilerAssetIsolation([string]$topologyDirectory) {
    foreach ($name in @('Contracts', 'Orders', 'Payments', 'Host')) {
        $outputDirectory = Join-Path $topologyDirectory "TinyBus.Sample.$name/bin/Release/net8.0"
        if (Test-Path -LiteralPath (Join-Path $outputDirectory 'TinyBus.SourceGen.dll')) {
            throw "The generator reached the $name runtime output."
        }
    }
}

function Test-TopologyDiagnostic([string]$hostProject, [string[]]$properties, [string]$scenario, [string]$expectedId) {
    $template = Join-Path $PSScriptRoot "Topology/$scenario.cs.template"
    $invalidHandler = Join-Path (Split-Path $hostProject) 'InvalidHandler.cs'
    Copy-Item -LiteralPath $template -Destination $invalidHandler -Force

    $diagnostics = & dotnet build $hostProject -c Release --no-restore @properties -warnaserror 2>&1
    $diagnosticExitCode = $LASTEXITCODE
    $diagnostics | Set-Content -LiteralPath (Join-Path $RunDirectory "$scenario-build.log")
    $errors = [regex]::Matches(($diagnostics -join "`n"), 'error ([A-Z]+\d+):')
    $unexpectedErrors = @($errors | Where-Object { $_.Groups[1].Value -ne $expectedId })

    if ($diagnosticExitCode -eq 0 -or $errors.Count -eq 0 -or $unexpectedErrors.Count -ne 0) {
        throw "Expected only $expectedId errors for $scenario.`n$($diagnostics -join "`n")"
    }

    if (($diagnostics -join "`n") -notmatch 'TinyBus.Sample.Payments::') {
        throw "The $scenario diagnostic did not identify the referenced handler assembly."
    }
}

Invoke-TopologyVerification
