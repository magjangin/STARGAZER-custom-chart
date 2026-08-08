# 게임 IL2CPP 인터롭 어셈블리를 decompiled/ 폴더로 디컴파일한다.
#   decompiled/.raw/<어셈블리>/  - ILSpy 원본 출력 (본문은 전부 인터롭 스텁)
#   decompiled/<어셈블리>/       - 본문을 걷어낸 시그니처 전용 버전
# 필요 도구: dotnet tool install -g ilspycmd, python

$ErrorActionPreference = 'Stop'

$gamePath = if ($env:GAME_PATH) { $env:GAME_PATH } else { 'H:\steam\steamapps\common\Sixtar Gate STARGAZER' }
$asmDir   = Join-Path $gamePath 'MelonLoader\Il2CppAssemblies'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outRoot  = Join-Path $repoRoot 'decompiled'
$rawRoot  = Join-Path $outRoot '.raw'
$stripper = Join-Path $PSScriptRoot 'strip_bodies.py'

# 디컴파일 대상. 게임 고유 코드만 추린 목록이며, 다른 어셈블리가 필요하면 여기에 추가한다.
$targets = @(
    'Il2CppMikazuki',
    'Il2CppMikazuki.Player',
    'Assembly-CSharp',
    'Assembly-CSharp-firstpass'
)

if (-not (Test-Path $asmDir)) {
    throw "Il2CppAssemblies 폴더를 찾을 수 없음: $asmDir"
}
if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) {
    throw "ilspycmd가 없음. 'dotnet tool install -g ilspycmd'로 설치할 것."
}
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    throw "python이 없음. 시그니처 추출에 필요함."
}

foreach ($name in $targets) {
    $dll = Join-Path $asmDir "$name.dll"
    if (-not (Test-Path $dll)) {
        Write-Warning "건너뜀 (DLL 없음): $dll"
        continue
    }

    $rawDir = Join-Path $rawRoot $name
    $outDir = Join-Path $outRoot $name
    foreach ($d in @($rawDir, $outDir)) {
        if (Test-Path $d) { Remove-Item $d -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $d | Out-Null
    }

    Write-Host "[디컴파일] $name"
    ilspycmd --disable-updatecheck --nested-directories -p -o $rawDir -r $asmDir $dll | Out-Null

    Write-Host "[시그니처] $name"
    python $stripper $rawDir $outDir
}

Write-Host ''
Get-ChildItem $outRoot -Directory -Exclude '.raw' | ForEach-Object {
    $files = Get-ChildItem $_.FullName -Recurse -Filter *.cs
    [pscustomobject]@{
        Assembly = $_.Name
        Files    = $files.Count
        MB       = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 2)
    }
} | Format-Table -AutoSize
