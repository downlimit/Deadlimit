$ErrorActionPreference = 'Stop'

$path = 'internal/src/Deadlimit/Core/RetailVmdlInheritance.cs'
$text = Get-Content -LiteralPath $path -Raw

$regexInsertPoint = @'
    public static string? FindRetailVmdl(ProjectManifest manifest)
'@
$regexBlock = @'
    private static readonly Regex VmatTextureSourceRegex = new(
        "^[ \\t]*\\\"?(?:Texture|g_t)[A-Za-z0-9_]*\\\"?[ \\t]*(?:=[ \\t]*)?\\\"(?<path>[^\\\"\\r\\n]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly HashSet<string> RetailTextureSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".tga",
        ".jpg",
        ".jpeg",
        ".tif",
        ".tiff",
        ".exr",
        ".vtex",
    };

'@
if (-not $text.Contains($regexInsertPoint)) { throw 'FindRetailVmdl insertion point was not found.' }
$text = $text.Replace($regexInsertPoint, $regexBlock + $regexInsertPoint)

$old = @'
        var destinationFolder = SafePath.ResolveUnderRoot(
            addonContentRoot,
            resourceFolder,
            "Retail VMDL destination folder");

        Directory.CreateDirectory(destinationFolder);

        var copied = 0;
'@
$new = @'
        var destinationFolder = SafePath.ResolveUnderRoot(
            addonContentRoot,
            resourceFolder,
            "Retail VMDL destination folder");
        var sourceRoot = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            manifest.SourceDumpFolderName,
            "Project source-dump folder");

        Directory.CreateDirectory(destinationFolder);

        var copied = 0;
'@
if (-not $text.Contains($old)) { throw 'CopyRetailModelSourceTree destination block was not found.' }
$text = $text.Replace($old, $new)

$old = @'
            File.Copy(sourceFile, destination, overwrite: true);
            copied++;
        }

        var destinationVmdl = Path.Combine(destinationFolder, Path.GetFileName(sourceVmdl));
'@
$new = @'
            File.Copy(sourceFile, destination, overwrite: true);
            copied++;
        }

        copied += CopyExternalRetailTextureDependencies(
            sourceFolder,
            sourceRoot,
            addonContentRoot);

        var destinationVmdl = Path.Combine(destinationFolder, Path.GetFileName(sourceVmdl));
'@
if (-not $text.Contains($old)) { throw 'CopyRetailModelSourceTree copy loop tail was not found.' }
$text = $text.Replace($old, $new)

$insertBefore = @'
    public static IReadOnlyList<RetailRenderMeshEntry> ReadRenderMeshes(string vmdlPath)
'@
$helper = @'
    private static int CopyExternalRetailTextureDependencies(
        string sourceFolder,
        string sourceRoot,
        string addonContentRoot)
    {
        var copied = 0;
        var copiedResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var vmatPath in Directory.EnumerateFiles(sourceFolder, "*.vmat", SearchOption.AllDirectories))
        {
            var vmatText = File.ReadAllText(vmatPath);
            foreach (Match match in VmatTextureSourceRegex.Matches(vmatText))
            {
                var resourcePath = NormalizeResourcePath(match.Groups["path"].Value);
                if (resourcePath.Length == 0
                    || resourcePath.Contains(':', StringComparison.Ordinal)
                    || !RetailTextureSourceExtensions.Contains(Path.GetExtension(resourcePath)))
                {
                    continue;
                }

                string sourcePath;
                try
                {
                    sourcePath = SafePath.ResolveUnderRoot(
                        sourceRoot,
                        resourcePath.Replace('/', Path.DirectorySeparatorChar),
                        "Retail VMAT texture dependency");
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (!File.Exists(sourcePath) || IsPathUnderRoot(sourceFolder, sourcePath))
                {
                    continue;
                }

                if (!copiedResources.Add(resourcePath))
                {
                    continue;
                }

                var destination = SafePath.ResolveUnderRoot(
                    addonContentRoot,
                    resourcePath.Replace('/', Path.DirectorySeparatorChar),
                    "Retail VMAT external texture dependency destination");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(sourcePath, destination, overwrite: true);
                copied++;
            }
        }

        return copied;
    }

    private static bool IsPathUnderRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

'@
if (-not $text.Contains($insertBefore)) { throw 'ReadRenderMeshes insertion point was not found.' }
$text = $text.Replace($insertBefore, $helper + $insertBefore)

Set-Content -LiteralPath $path -Value $text -Encoding utf8NoBOM -NoNewline

$smoke = @'
$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.RetailVmdlInheritance', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$copy = $type.GetMethod('CopyExternalRetailTextureDependencies', $flags)
if ($null -eq $copy) { throw 'CopyExternalRetailTextureDependencies was not found.' }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-retail-dep-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $temp '0source'
$heroFolder = Join-Path $sourceRoot 'models\heroes_wip\vampirebat'
$heroMaterials = Join-Path $heroFolder 'materials'
$externalTexture = Join-Path $sourceRoot 'models\heroes_wip\bookworm\materials\outline_layout.png'
$addonRoot = Join-Path $temp 'addon'
try {
    New-Item -ItemType Directory -Path $heroMaterials -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $externalTexture) -Force | Out-Null
    New-Item -ItemType Directory -Path $addonRoot -Force | Out-Null

    $vmat = @"
Layer0
{
    "TextureColor" "models/heroes_wip/vampirebat/materials/body.png"
    "TextureDetail" "models/heroes_wip/bookworm/materials/outline_layout.png"
}
"@
    Set-Content -LiteralPath (Join-Path $heroMaterials 'vampirebat_bag.vmat') -Value $vmat -Encoding utf8NoBOM
    [IO.File]::WriteAllBytes($externalTexture, [byte[]](1,2,3,4))

    $count = [int]$copy.Invoke($null, @($heroFolder, $sourceRoot, $addonRoot))
    if ($count -ne 1) { throw "Expected one external texture dependency copy, got $count." }

    $expected = Join-Path $addonRoot 'models\heroes_wip\bookworm\materials\outline_layout.png'
    if (-not (Test-Path -LiteralPath $expected)) {
        throw "Cross-hero texture dependency was not copied to its Source 2 resource path: $expected"
    }

    $bytes = [IO.File]::ReadAllBytes($expected)
    if ($bytes.Length -ne 4 -or $bytes[0] -ne 1 -or $bytes[3] -ne 4) {
        throw 'Copied cross-hero texture dependency content does not match source.'
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Retail external texture dependency copy smoke passed.'
'@
Set-Content -LiteralPath 'internal/tests/retail-external-texture-copy-smoke.ps1' -Value $smoke -Encoding utf8NoBOM -NoNewline
