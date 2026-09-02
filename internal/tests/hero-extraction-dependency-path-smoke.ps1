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