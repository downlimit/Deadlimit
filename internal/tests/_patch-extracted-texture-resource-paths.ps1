$ErrorActionPreference = 'Stop'

$path = 'internal/src/Deadlimit/Core/HeroExtractionService.cs'
$text = Get-Content -LiteralPath $path -Raw
$old = @'
        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            var additionalPath = additionalFile.KeepFullPath
                ? SafePath.ResolveUnderRoot(
                    outputRoot,
                    ToWindowsPath(additionalFile.FileName),
                    "Additional extracted resource")
                : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(additionalFile.FileName));

            DumpContentFile(outputRoot, additionalPath, additionalFile);
        }
'@
$new = @'
        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            var additionalFileName = NormalizeResourcePath(additionalFile.FileName);
            var preserveTextureResourceDirectory = additionalFile is TextureContentFile
                && additionalFileName.Contains('/');
            var additionalPath = additionalFile.KeepFullPath || preserveTextureResourceDirectory
                ? SafePath.ResolveUnderRoot(
                    outputRoot,
                    ToWindowsPath(additionalFileName),
                    "Additional extracted resource")
                : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(additionalFileName));

            DumpContentFile(outputRoot, additionalPath, additionalFile);
        }
'@
if (-not $text.Contains($old)) { throw 'Expected DumpContentFile AdditionalFiles block was not found.' }
$text = $text.Replace($old, $new)
Set-Content -LiteralPath $path -Value $text -Encoding utf8NoBOM -NoNewline

$smoke = @'
$ErrorActionPreference = 'Stop'
$path = 'internal/src/Deadlimit/Core/HeroExtractionService.cs'
$text = Get-Content -LiteralPath $path -Raw
$required = @(
    'var additionalFileName = NormalizeResourcePath(additionalFile.FileName);',
    'additionalFile is TextureContentFile',
    "additionalFileName.Contains('/')",
    'additionalFile.KeepFullPath || preserveTextureResourceDirectory',
    'ToWindowsPath(additionalFileName)'
)
foreach ($pattern in $required) {
    if (-not $text.Contains($pattern)) { throw "Hero extraction dependency path contract missing: $pattern" }
}
if ($text.Contains('ToWindowsPath(additionalFile.FileName)')) {
    throw 'Old AdditionalFiles path handling still remains.'
}
Write-Host 'Hero extraction dependency path smoke passed.'
'@
Set-Content -LiteralPath 'internal/tests/hero-extraction-dependency-path-smoke.ps1' -Value $smoke -Encoding utf8NoBOM -NoNewline
