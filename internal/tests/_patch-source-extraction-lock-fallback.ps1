$ErrorActionPreference = 'Stop'

$sourcePath = 'internal/src/Deadlimit/Core/HeroExtractionService.cs'
$text = Get-Content -LiteralPath $sourcePath -Raw

$oldPublish = @'
            progress?.Report(new HeroExtractionProgress("Publishing refreshed 0source..."));

            DeleteDirectoryIfExists(previousFolder);
            if (Directory.Exists(outputFolder))
            {
                Directory.Move(outputFolder, previousFolder);
            }

            try
            {
                Directory.Move(stagingFolder, outputFolder);
            }
            catch
            {
                if (!Directory.Exists(outputFolder) && Directory.Exists(previousFolder))
                {
                    Directory.Move(previousFolder, outputFolder);
                }

                throw;
            }
'@
$newPublish = @'
            progress?.Report(new HeroExtractionProgress("Publishing refreshed 0source..."));
            PublishRefreshedSource(
                stagingFolder,
                outputFolder,
                previousFolder,
                progress);
'@
if (-not $text.Contains($oldPublish)) { throw 'Hero extraction publish block was not found.' }
$text = $text.Replace($oldPublish, $newPublish)

$insertBefore = @'
    private static void DeleteDirectoryIfExists(string path)
'@
$helpers = @'
    private static void PublishRefreshedSource(
        string stagingFolder,
        string outputFolder,
        string previousFolder,
        IProgress<HeroExtractionProgress>? progress)
    {
        DeleteDirectoryIfExists(previousFolder);

        if (!Directory.Exists(outputFolder))
        {
            Directory.Move(stagingFolder, outputFolder);
            return;
        }

        try
        {
            Directory.Move(outputFolder, previousFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progress?.Report(new HeroExtractionProgress(
                "The existing 0source folder is busy; refreshing its contents in place..."));
            PublishRefreshedSourceInPlace(stagingFolder, outputFolder, previousFolder);
            return;
        }

        try
        {
            Directory.Move(stagingFolder, outputFolder);
        }
        catch
        {
            if (!Directory.Exists(outputFolder) && Directory.Exists(previousFolder))
            {
                Directory.Move(previousFolder, outputFolder);
            }

            throw;
        }
    }

    private static void PublishRefreshedSourceInPlace(
        string stagingFolder,
        string outputFolder,
        string previousFolder)
    {
        CopyDirectoryFiles(outputFolder, previousFolder, "back up current 0source");

        var stagedRelativeFiles = Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(stagingFolder, path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var existingFile in Directory.EnumerateFiles(outputFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(outputFolder, existingFile);
            if (stagedRelativeFiles.Contains(relative))
            {
                continue;
            }

            try
            {
                File.Delete(existingFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Could not remove stale extracted source file because it is in use or access is denied: {existingFile}",
                    ex);
            }
        }

        foreach (var stagedFile in Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stagingFolder, stagedFile);
            var destination = SafePath.ResolveUnderRoot(
                outputFolder,
                relative,
                "Refreshed extracted source file");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            try
            {
                File.Copy(stagedFile, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Could not refresh extracted source file because it is in use or access is denied: {destination}",
                    ex);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(outputFolder, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, recursive: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A process may hold an otherwise empty folder open. Empty stale folders are harmless.
            }
        }

        DeleteDirectoryIfExists(stagingFolder);
    }

    private static void CopyDirectoryFiles(string sourceFolder, string destinationFolder, string operation)
    {
        Directory.CreateDirectory(destinationFolder);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceFolder, sourceFile);
            var destination = SafePath.ResolveUnderRoot(
                destinationFolder,
                relative,
                "Source extraction backup file");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            try
            {
                File.Copy(sourceFile, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Could not {operation}; a source file is in use or access is denied: {sourceFile}",
                    ex);
            }
        }
    }

'@
if (-not $text.Contains($insertBefore)) { throw 'DeleteDirectoryIfExists insertion point was not found.' }
$text = $text.Replace($insertBefore, $helpers + $insertBefore)
Set-Content -LiteralPath $sourcePath -Value $text -Encoding utf8NoBOM -NoNewline

$smoke = @'
$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.HeroExtractionService', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$publish = $type.GetMethod('PublishRefreshedSourceInPlace', $flags)
if ($null -eq $publish) { throw 'PublishRefreshedSourceInPlace was not found.' }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-source-publish-' + [Guid]::NewGuid().ToString('N'))
$staging = Join-Path $temp 'staging'
$output = Join-Path $temp '0source'
$previous = Join-Path $temp '0source.previous'
try {
    New-Item -ItemType Directory -Path (Join-Path $staging 'models\hero') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $output 'models\hero') -Force | Out-Null

    Set-Content -LiteralPath (Join-Path $output 'models\hero\keep.txt') -Value 'old' -NoNewline
    Set-Content -LiteralPath (Join-Path $output 'models\hero\stale.txt') -Value 'stale' -NoNewline
    Set-Content -LiteralPath (Join-Path $staging 'models\hero\keep.txt') -Value 'new' -NoNewline
    Set-Content -LiteralPath (Join-Path $staging 'models\hero\added.txt') -Value 'added' -NoNewline

    $args = [object[]]@([string]$staging, [string]$output, [string]$previous)
    $publish.Invoke($null, $args) | Out-Null

    if ((Get-Content -LiteralPath (Join-Path $output 'models\hero\keep.txt') -Raw) -ne 'new') { throw 'Existing file was not refreshed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $output 'models\hero\added.txt'))) { throw 'New file was not published.' }
    if (Test-Path -LiteralPath (Join-Path $output 'models\hero\stale.txt')) { throw 'Stale file survived in-place refresh.' }
    if ((Get-Content -LiteralPath (Join-Path $previous 'models\hero\keep.txt') -Raw) -ne 'old') { throw 'Previous source backup was not preserved.' }
    if (-not (Test-Path -LiteralPath (Join-Path $previous 'models\hero\stale.txt'))) { throw 'Previous source backup is incomplete.' }
    if (Test-Path -LiteralPath $staging) { throw 'Staging folder survived successful in-place refresh.' }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Hero extraction in-place publish smoke passed.'
'@
Set-Content -LiteralPath 'internal/tests/hero-extraction-publish-smoke.ps1' -Value $smoke -Encoding utf8NoBOM -NoNewline

$preparePath = 'internal/tests/prepare-behavior-smoke.ps1'
$prepare = Get-Content -LiteralPath $preparePath -Raw
$anchor = "& (Join-Path `$PSScriptRoot 'hero-extraction-dependency-path-smoke.ps1')"
$replacement = $anchor + "`n& (Join-Path `$PSScriptRoot 'hero-extraction-publish-smoke.ps1')"
if (-not $prepare.Contains($anchor)) { throw 'Prepare smoke extraction dependency anchor was not found.' }
if (-not $prepare.Contains("hero-extraction-publish-smoke.ps1")) {
    $prepare = $prepare.Replace($anchor, $replacement)
}
Set-Content -LiteralPath $preparePath -Value $prepare -Encoding utf8NoBOM -NoNewline
