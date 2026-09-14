$ErrorActionPreference = 'Stop'
$shadeRoot = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $shadeRoot 'tools\New-OutlinePreviewMesh.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-imported-outline-' + [Guid]::NewGuid().ToString('N'))

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $source = Join-Path $testRoot 'source.obj'
    $narrow = Join-Path $testRoot 'narrow.obj'
    $wide = Join-Path $testRoot 'wide.obj'
    $repeat = Join-Path $testRoot 'repeat.obj'
    $welded = Join-Path $testRoot 'welded.obj'
    $sourceLines = @(
        '# imported character-like fixture'
        'mtllib character.mtl'
        'o Character'
        'v 0 0 0 0.1 0.2 0.3'
        'v 1 0 0 0.4 0.5 0.6'
        'v 0 1 0 0.7 0.8 0.9'
        'vt 0 0'
        'vt 1 0'
        'vt 0 1'
        'vn 0 0 2'
        'vn 0 2 0'
        'g Body'
        'usemtl coat'
        'f 1/1/1 2/2/1 3/3/1'
        'g HardEdge'
        'usemtl skin'
        'f -3/1/2 -1/3/2 -2/2/2'
    )
    [IO.File]::WriteAllLines($source, $sourceLines, [Text.UTF8Encoding]::new($false))

    & $tool -SourceMesh $source -OutputPath $narrow -OutlineWidth 0.03 -ExpansionMode SplitRenderNormal | Out-Null
    & $tool -SourceMesh $source -OutputPath $wide -OutlineWidth 0.08 -ExpansionMode SplitRenderNormal | Out-Null
    & $tool -SourceMesh $source -OutputPath $repeat -OutlineWidth 0.03 -ExpansionMode SplitRenderNormal | Out-Null
    & $tool -SourceMesh $source -OutputPath $welded -OutlineWidth 0.03 -ExpansionMode WeldedPosition | Out-Null

    Assert-True ((Get-FileHash $narrow).Hash -eq (Get-FileHash $repeat).Hash) 'Identical inputs are not byte-deterministic.'
    $sourceRead = [IO.File]::ReadAllLines($source)
    $narrowRead = [IO.File]::ReadAllLines($narrow)
    Assert-True ($narrowRead.Count -gt $sourceRead.Count) 'No outline shell was appended.'
    for ($index = 0; $index -lt $sourceRead.Count; $index++) {
        Assert-True ($sourceRead[$index] -ceq $narrowRead[$index]) "Source line $($index + 1) changed."
    }
    Assert-True (($narrowRead | Where-Object { $_ -eq 'usemtl coat' }).Count -eq 1) 'Original coat material assignment changed.'
    Assert-True (($narrowRead | Where-Object { $_ -eq 'usemtl skin' }).Count -eq 1) 'Original skin material assignment changed.'
    Assert-True (($narrowRead | Where-Object { $_ -eq 'usemtl __deadlimit_outline' }).Count -eq 1) 'Reserved outline material is missing.'

    $shellVertexLines = @($narrowRead | Where-Object { $_ -match '^v \d+\.\d{8} ' })
    Assert-True ($shellVertexLines.Count -eq 6) 'Split render normals did not create distinct shell vertices.'
    Assert-True ($shellVertexLines[0] -eq 'v 0.00000000 0.00000000 0.03000000') 'Normal normalization/displacement is incorrect.'
    Assert-True ($shellVertexLines[3] -eq 'v 0.00000000 0.03000000 0.00000000') 'Hard-edge split normal displacement is incorrect.'
    $shellFaces = @($narrowRead | Where-Object { $_ -match '^f [4-9]' })
    Assert-True ($shellFaces[0] -eq 'f 6/3/1 5/2/1 4/1/1') 'First shell face winding or UV/normal references are incorrect.'
    Assert-True ($shellFaces[1] -eq 'f 9/2/2 8/3/2 7/1/2') 'Negative source indices were not resolved correctly.'

    $wideRead = [IO.File]::ReadAllLines($wide)
    for ($index = 0; $index -lt $sourceRead.Count; $index++) {
        Assert-True ($sourceRead[$index] -ceq $wideRead[$index]) 'Changing width modified source mesh content.'
    }
    Assert-True (($wideRead | Where-Object { $_ -eq 'v 0.00000000 0.00000000 0.08000000' }).Count -eq 1) 'Wide displacement was not applied.'

    $weldedRead = [IO.File]::ReadAllLines($welded)
    $weldedVertices = @($weldedRead | Where-Object { $_ -match '^v \d+\.\d{8} ' })
    Assert-True ($weldedVertices.Count -eq 3) 'WeldedPosition did not emit one shell vertex per source position.'
    Assert-True ($weldedVertices[0] -eq 'v 0.00000000 0.02121320 0.02121320') 'Welded render-normal average is incorrect.'
    $weldedFaces = @($weldedRead | Where-Object { $_ -match '^f [4-6]' })
    Assert-True ($weldedFaces[0] -eq 'f 6/3/1 5/2/1 4/1/1') 'Welded first shell face is incorrect.'
    Assert-True ($weldedFaces[1] -eq 'f 5/2/2 6/3/2 4/1/2') 'Welded negative-index face is incorrect.'

    $invalid = Join-Path $testRoot 'invalid.obj'
    [IO.File]::WriteAllLines($invalid, @('v 0 0 0', 'v 1 0 0', 'v 0 1 0', 'f 1 2 3'), [Text.UTF8Encoding]::new($false))
    $rejected = $false
    try {
        & $tool -SourceMesh $invalid -OutputPath (Join-Path $testRoot 'invalid-preview.obj') -OutlineWidth 0.03 | Out-Null
    }
    catch {
        $rejected = $_.Exception.Message -like '*no render normals*'
    }
    Assert-True $rejected 'A mesh without render normals was not rejected predictably.'

    Write-Output 'Deadlimit Shade imported OBJ outline preview smoke passed.'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
