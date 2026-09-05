[CmdletBinding()]
param(
    [string]$Version = '0.1.0-beta.1',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot 'portable'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
$artifactsPrefix = $artifactsRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $outputRoot.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Portable output must stay under the repository artifacts folder: $artifactsRoot"
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Portable version is not a supported semantic version: $Version"
}

function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Get-StandardLicenseText([string]$Expression, [string]$CopyrightLine) {
    if ([string]::IsNullOrWhiteSpace($CopyrightLine)) {
        $CopyrightLine = 'Copyright (c) upstream contributors'
    }
    elseif (-not $CopyrightLine.StartsWith('Copyright', [StringComparison]::OrdinalIgnoreCase) -and
        -not $CopyrightLine.StartsWith('©', [StringComparison]::OrdinalIgnoreCase)) {
        $CopyrightLine = "Copyright (c) $CopyrightLine"
    }

    if ($Expression -eq 'MIT') {
        return @"
MIT License

$CopyrightLine

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"@
    }

    if ($Expression -eq 'BSD-2-Clause') {
        return @"
BSD 2-Clause License

$CopyrightLine

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
"@
    }

    return $null
}

function Get-ResolvedPackages([string]$ProjectPath) {
    $jsonText = (& dotnet list $ProjectPath package --include-transitive --format json --no-restore) -join "`n"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the resolved NuGet package graph.'
    }
    $graph = $jsonText | ConvertFrom-Json
    $packages = foreach ($project in @($graph.projects)) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                [pscustomobject]@{
                    Id = [string]$package.id
                    Version = [string]$package.resolvedVersion
                }
            }
        }
    }
    return @($packages | Sort-Object Id, Version -Unique)
}

function Copy-NuGetLicensePayload([string]$ProjectPath, [string]$DestinationRoot) {
    $nugetLine = (& dotnet nuget locals global-packages --list) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $nugetLine -notmatch '(?m)^global-packages:\s*(.+)$') {
        throw 'Could not resolve the NuGet global-packages folder.'
    }
    $nugetRoot = $Matches[1].Trim()
    [IO.Directory]::CreateDirectory($DestinationRoot) | Out-Null

    foreach ($package in Get-ResolvedPackages $ProjectPath) {
        $packageRoot = Join-Path (Join-Path $nugetRoot $package.Id.ToLowerInvariant()) $package.Version.ToLowerInvariant()
        if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
            throw "Resolved NuGet package is missing from the local cache: $($package.Id) $($package.Version)"
        }

        $destination = Join-Path $DestinationRoot "$($package.Id)-$($package.Version)"
        [IO.Directory]::CreateDirectory($destination) | Out-Null
        $nuspecPath = Get-ChildItem -LiteralPath $packageRoot -Filter '*.nuspec' -File -Force | Select-Object -First 1
        if ($null -eq $nuspecPath) {
            throw "NuGet package has no nuspec metadata: $($package.Id) $($package.Version)"
        }
        Copy-Item -LiteralPath $nuspecPath.FullName -Destination (Join-Path $destination $nuspecPath.Name)

        [xml]$nuspec = Get-Content -LiteralPath $nuspecPath.FullName -Raw
        $namespace = [Xml.XmlNamespaceManager]::new($nuspec.NameTable)
        $namespace.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
        $metadata = $nuspec.SelectSingleNode('/n:package/n:metadata', $namespace)
        $license = $metadata.SelectSingleNode('n:license', $namespace)
        $licenseType = if ($null -eq $license) { 'legacy/unspecified' } else { [string]$license.Attributes['type'].Value }
        $licenseValue = if ($null -eq $license) { '' } else { $license.InnerText.Trim() }
        $licenseUrlNode = $metadata.SelectSingleNode('n:licenseUrl', $namespace)
        $licenseUrl = if ($null -eq $licenseUrlNode) { '' } else { $licenseUrlNode.InnerText.Trim() }
        if ([string]::IsNullOrWhiteSpace($licenseValue) -and $licenseUrl -match '/(?<expression>MIT|BSD-2-Clause)$') {
            $licenseValue = $Matches.expression
            $licenseType = 'legacy URL resolved to SPDX expression'
        }
        elseif ([string]::IsNullOrWhiteSpace($licenseValue) -and
            $package.Id -eq 'K4os.Compression.LZ4' -and
            $licenseUrl -match '/K4os\.Compression\.LZ4/.*/LICENSE') {
            $licenseValue = 'MIT'
            $licenseType = 'audited legacy upstream license URL resolved to SPDX expression'
        }
        $copyrightNode = $metadata.SelectSingleNode('n:copyright', $namespace)
        $authorsNode = $metadata.SelectSingleNode('n:authors', $namespace)
        $projectUrlNode = $metadata.SelectSingleNode('n:projectUrl', $namespace)
        $repositoryNode = $metadata.SelectSingleNode('n:repository', $namespace)
        $repositoryUrl = if ($null -eq $repositoryNode -or $null -eq $repositoryNode.Attributes['url']) {
            ''
        }
        else {
            [string]$repositoryNode.Attributes['url'].Value
        }

        $metadataText = @(
            "Package: $($package.Id)",
            "Resolved version: $($package.Version)",
            "Declared license type: $licenseType",
            "Declared license value: $licenseValue",
            "Legacy license URL: $licenseUrl",
            "Copyright: $(if ($null -eq $copyrightNode) { '' } else { $copyrightNode.InnerText.Trim() })",
            "Authors: $(if ($null -eq $authorsNode) { '' } else { $authorsNode.InnerText.Trim() })",
            "Project URL: $(if ($null -eq $projectUrlNode) { '' } else { $projectUrlNode.InnerText.Trim() })",
            "Repository URL: $repositoryUrl",
            'The original nuspec and any license/notice files shipped in the NuGet package are included in this folder.'
        ) -join "`n"
        Write-Utf8NoBom (Join-Path $destination 'PACKAGE-METADATA.txt') ($metadataText + "`n")

        $noticeFiles = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Force |
            Where-Object {
                $_.Length -le 2MB -and
                $_.Name -match '^(?i:licen[cs]e|copying|notice)(?:\.|$)'
            })
        foreach ($noticeFile in $noticeFiles) {
            $relative = [IO.Path]::GetRelativePath($packageRoot, $noticeFile.FullName)
            $safeName = $relative.Replace('\', '__').Replace('/', '__')
            Copy-Item -LiteralPath $noticeFile.FullName -Destination (Join-Path $destination $safeName)
        }

        if ($noticeFiles.Count -eq 0) {
            $copyrightValue = if ($null -ne $copyrightNode -and -not [string]::IsNullOrWhiteSpace($copyrightNode.InnerText)) {
                $copyrightNode.InnerText.Trim()
            }
            elseif ($null -ne $authorsNode) {
                $authorsNode.InnerText.Trim()
            }
            else {
                "$($package.Id) contributors"
            }
            $standardLicense = Get-StandardLicenseText $licenseValue $copyrightValue
            if ([string]::IsNullOrWhiteSpace($standardLicense)) {
                throw "NuGet package has no bundled notice and its license expression is not supported by the release packager: $($package.Id) $($package.Version) ($licenseValue)"
            }
            Write-Utf8NoBom (Join-Path $destination 'SPDX-LICENSE.txt') ($standardLicense.Trim() + "`n")
        }
    }
}

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$projectPath = Join-Path $repoRoot 'internal\src\Deadlimit\Deadlimit.csproj'
$packageRoot = Join-Path $outputRoot 'Deadlimit-win-x64'
$archivePath = Join-Path $outputRoot 'Deadlimit-win-x64.zip'
$checksumPath = "$archivePath.sha256"
$installerPath = Join-Path $outputRoot 'Install-Deadlimit.cmd'
$installerChecksumPath = "$installerPath.sha256"

& dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $packageRoot `
    --nologo `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "Deadlimit portable publish failed with exit code $LASTEXITCODE."
}

foreach ($document in @(
    'LICENSE',
    'README.md',
    'README.ru.md',
    'CHANGELOG.md',
    'COMPATIBILITY.md',
    'SUPPORT.md',
    'SECURITY.md',
    'THIRD_PARTY_NOTICES.md'
)) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination (Join-Path $packageRoot $document)
}

$portableInternal = Join-Path $packageRoot 'internal'
[IO.Directory]::CreateDirectory($portableInternal) | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'internal\DeadlimitPortableUpdater.ps1') -Destination $portableInternal
Copy-Item -LiteralPath (Join-Path $repoRoot 'Update Deadlimit.cmd') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'Install-Deadlimit.cmd') -Destination $installerPath

Copy-NuGetLicensePayload $projectPath (Join-Path $packageRoot 'licenses')

$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Could not resolve the release source commit.'
}
$commitDate = (& git -C $repoRoot show -s --format=%cI HEAD).Trim()
$releaseMetadata = [ordered]@{
    product = 'Deadlimit Manager'
    version = $Version
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    sourceCommit = $commit
    sourceCommitDate = $commitDate
}
Write-Utf8NoBom (Join-Path $packageRoot 'release.json') (($releaseMetadata | ConvertTo-Json) + "`n")

$forbiddenPackageFiles = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Force |
    Where-Object {
        $_.Extension.ToLowerInvariant() -in @('.vpk', '.vmdl_c', '.vmat_c', '.vtex_c', '.vmesh_c', '.vanim_c', '.vagrp_c', '.vphys_c', '.dmx', '.fbx', '.max')
    })
if ($forbiddenPackageFiles.Count -gt 0) {
    $forbiddenPackageFiles | ForEach-Object { Write-Error "Forbidden portable package file: $($_.FullName)" }
    throw 'Portable package contains prohibited game or authoring content.'
}

$manifestEntries = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Force |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath($packageRoot, $_.FullName).Replace('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
Write-Utf8NoBom (Join-Path $packageRoot 'release-manifest.json') (($manifestEntries | ConvertTo-Json -Depth 4) + "`n")

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $packageRoot,
    $archivePath,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Utf8NoBom $checksumPath "$archiveHash  Deadlimit-win-x64.zip`n"
$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Utf8NoBom $installerChecksumPath "$installerHash  Install-Deadlimit.cmd`n"

Write-Host "Portable package: $archivePath"
Write-Host "SHA-256: $archiveHash"
Write-Host "Installer SHA-256: $installerHash"
Write-Host "Files: $($manifestEntries.Count)"
