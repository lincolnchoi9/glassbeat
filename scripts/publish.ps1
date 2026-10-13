param([string]$ArtifactRoot, [string]$BuildRoot, [string]$PackageCache)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/GlassBeat/GlassBeat.csproj'
if (!$ArtifactRoot) { $ArtifactRoot = Join-Path $root 'artifacts' }
if ($PackageCache) { $env:NUGET_PACKAGES = $PackageCache }
$output = Join-Path $ArtifactRoot 'GlassBeat-win-x64'
$buildArguments = @()
if ($BuildRoot) { $buildArguments += "-p:BaseOutputPath=$BuildRoot/bin/"; $buildArguments += "-p:BaseIntermediateOutputPath=$BuildRoot/obj/" }
dotnet publish $project -c Release -r win-x64 --self-contained true -o $output @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $output -Recurse -Force
Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $ArtifactRoot 'GlassBeat-win-x64.zip') -Force
