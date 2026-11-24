<#
.SYNOPSIS
  STARGAZER custom chart 릴리즈: 버전 확인 → Release 빌드 → 단위 테스트 → 본체 검사 → zip → (선택) 태그·GitHub 릴리즈.

.DESCRIPTION
  버전은 "STARGAZER custom chart\STARGAZER custom chart.csproj" 의 <Version> 하나만 기준으로 삼고, 태그는 v<버전> 입니다.
  Properties\AssemblyInfo.cs 의 MelonInfo 버전과 CHANGELOG.md 의 "## [<버전>]" 항목이 같은 버전인지도 확인합니다.

  기본 실행은 아무것도 게시하지 않습니다: 검증 후 dist\STARGAZER-custom-chart-v<버전>.zip 과 릴리즈 노트 초안만 만듭니다.
  -Publish 를 주면 main 이 origin/main 과 같고 작업 트리가 깨끗한지 확인한 뒤
  주석 태그를 만들어 origin 에 푸시하고 gh 로 GitHub 릴리즈를 만들어 zip 을 올립니다.

  모드 빌드에는 게임의 MelonLoader\net6 와 Il2CppAssemblies 가 필요해서 GitHub Actions 에서는 빌드할 수 없습니다.
  그래서 게임이 설치된 PC 에서 실행하는 로컬 스크립트입니다. (CI 는 단위 테스트와 tools\bodycheck 만 돌립니다.)

  zip 에는 "STARGAZER custom chart.dll" 하나만 넣습니다(v1.0.0 릴리즈와 같은 구성). 게임 파일·덤프·디컴파일 코드는 넣지 않습니다.

.PARAMETER Publish
  태그 생성·푸시와 GitHub 릴리즈 생성까지 합니다. 없으면 검증과 zip 까지만.

.PARAMETER GamePath
  게임 폴더. 없으면 build.bat 과 같은 순서(환경변수 GAME_PATH → H:\steam\steamapps\common\Sixtar Gate STARGAZER)를 따릅니다.

.EXAMPLE
  .\tools\release.ps1              # 검증 + dist\STARGAZER-custom-chart-v<버전>.zip
  .\tools\release.ps1 -Publish     # 위 + 태그 v<버전> 푸시 + GitHub 릴리즈
#>
[CmdletBinding()]
param(
    [switch]$Publish,
    [string]$GamePath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Step($msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }
function Invoke-Native {
    param([string]$What, [string]$Exe, [string[]]$Arguments)
    # Windows PowerShell 5.1 은 $ErrorActionPreference='Stop' 일 때 네이티브 명령의 stderr 한 줄도 오류로 던진다.
    # git push 는 성공해도 "To https://..." 진행 출력을 stderr 로 내서, 태그 푸시가 성공했는데 실패로 보고되고
    # 로컬 태그까지 지워졌다(v1.2.0 릴리즈 때 실측). 성공 여부는 종료 코드로만 판단한다.
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Exe @Arguments 2>&1 | ForEach-Object { Write-Host $_ } }
    finally { $ErrorActionPreference = $prev }
    if ($LASTEXITCODE -ne 0) { throw "$What 실패 (종료 코드 $LASTEXITCODE)" }
}
function Write-Utf8NoBom($path, $text) {
    # Windows PowerShell 5.1 의 Set-Content -Encoding utf8 은 BOM 을 붙여 gh 릴리즈 노트 첫 글자가 깨질 수 있음
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
}

# ---------------------------------------------------------------- 1. 버전
Step '버전 확인'
$csprojPath = Join-Path $root 'STARGAZER custom chart\STARGAZER custom chart.csproj'
[xml]$csproj = Get-Content $csprojPath -Raw -Encoding UTF8
$version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "csproj 에 <Version> 이 없습니다: $csprojPath" }
$version = $version.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "버전 형식이 x.y.z 가 아닙니다: $version" }
$tag = "v$version"

$asmInfoPath = Join-Path $root 'STARGAZER custom chart\Properties\AssemblyInfo.cs'
$asmInfo = Get-Content $asmInfoPath -Raw -Encoding UTF8
if ($asmInfo -notmatch 'MelonInfo\(\s*typeof\([^)]*\)\s*,\s*"[^"]*"\s*,\s*"(?<v>[^"]+)"') {
    throw "AssemblyInfo.cs 에서 MelonInfo 버전을 찾지 못했습니다: $asmInfoPath"
}
if ($Matches['v'] -ne $version) { throw "버전 불일치: csproj $version / AssemblyInfo.cs MelonInfo $($Matches['v'])" }

$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw -Encoding UTF8
$entryPattern = "(?ms)^## \[$([regex]::Escape($version))\][^\r\n]*\r?\n(?<body>.*?)(?=^## |\z)"
# 본문은 다음 "## " 제목(버전 항목이든 날짜 작업 기록이든)에서 끊는다. "## [" 로만 끊으면 아래 날짜 항목까지 릴리즈 노트에 섞인다.
$changelogBody = if ($changelog -match $entryPattern) { $Matches['body'].Trim() } else { $null }
if (-not $changelogBody) {
    if ($Publish) { throw "CHANGELOG.md 에 '## [$version]' 항목이 없습니다." }
    Write-Warning "CHANGELOG.md 에 '## [$version]' 항목이 없습니다 (-Publish 때는 중단됩니다)."
}
Write-Host "버전 $version (태그 $tag), csproj·AssemblyInfo.cs·CHANGELOG 일치"

# ---------------------------------------------------------------- 2. git 상태 (게시할 때만 강제)
Step 'git 상태 확인'
Invoke-Native 'git fetch' git @('fetch', 'origin', '--tags', '--quiet')
$dirty = git status --porcelain
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
$head = (git rev-parse HEAD).Trim()
$originMain = (git rev-parse origin/main).Trim()
$localTag = git tag --list $tag
$remoteTag = git ls-remote --tags origin "refs/tags/$tag"

$problems = @()
if ($dirty) { $problems += '작업 트리에 커밋하지 않은 변경이 있습니다.' }
if ($branch -ne 'main') { $problems += "현재 브랜치가 main 이 아닙니다($branch)." }
if ($head -ne $originMain) { $problems += 'HEAD 가 origin/main 과 다릅니다(푸시·머지 또는 pull 필요).' }
if ($localTag -or $remoteTag) { $problems += "태그 $tag 가 이미 있습니다. csproj·AssemblyInfo.cs·CHANGELOG 의 버전을 올리세요." }
if ($problems) {
    if ($Publish) { throw ("게시할 수 없습니다:`n - " + ($problems -join "`n - ")) }
    $problems | ForEach-Object { Write-Warning $_ }
} else { Write-Host "main = origin/main ($($head.Substring(0, 7))), 태그 $tag 없음" }

if ($Publish) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh(GitHub CLI)가 없습니다. https://cli.github.com 설치 후 gh auth login.' }
    Invoke-Native 'gh auth status' gh @('auth', 'status')
}

# ---------------------------------------------------------------- 3. 빌드·테스트·본체 검사
if (-not $GamePath) { $GamePath = if ($env:GAME_PATH) { $env:GAME_PATH } else { 'H:\steam\steamapps\common\Sixtar Gate STARGAZER' } }
if (-not (Test-Path (Join-Path $GamePath 'MelonLoader\Il2CppAssemblies'))) {
    throw "게임 인터롭이 없습니다: $GamePath\MelonLoader\Il2CppAssemblies (게임을 MelonLoader 와 한 번 실행하거나 -GamePath 지정)"
}
$gameArgs = @("-p:GamePath=$GamePath")
$modProj = 'STARGAZER custom chart\STARGAZER custom chart.csproj'
$testProj = 'STARGAZER custom chart.Tests\STARGAZER custom chart.Tests.csproj'

Step 'Release 빌드 (게임 Mods 폴더로 복사하지 않음)'
Invoke-Native '빌드' dotnet (@('build', $modProj, '-c', 'Release', '-p:SkipDeploy=true', '-nologo') + $gameArgs)
$dll = Join-Path $root 'STARGAZER custom chart\bin\Release\net6.0\STARGAZER custom chart.dll'
if (-not (Test-Path $dll)) { throw "DLL 없음: $dll" }

Step '단위 테스트'
Invoke-Native '단위 테스트' dotnet @('test', $testProj, '-c', 'Release', '-nologo')

Step '모드 본체 net6 검사 (tools\bodycheck, 게임 인터롭 포함)'
$bodyArgs = @('run', '--project', 'tools\bodycheck', '-c', 'Release', '--', '--interop', (Join-Path $GamePath 'MelonLoader\Il2CppAssemblies'))
Invoke-Native '본체 검사' dotnet $bodyArgs

# ---------------------------------------------------------------- 4. 패키징
Step 'zip 패키징'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zipName = "STARGAZER-custom-chart-$tag.zip"
$zip = Join-Path $dist $zipName
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $dll -DestinationPath $zip
$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$dllHash = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "생성: $zip"
Write-Host "SHA256 zip $zipHash"
Write-Host "SHA256 dll $dllHash"

# 게임 기준 빌드는 AGENTS.md 1절에서 읽는다 (고정 빌드를 바꾸면 그 문서와 GameVersionLogic.PinnedSteamBuildId 를 먼저 갱신)
$agents = Get-Content (Join-Path $root 'AGENTS.md') -Raw -Encoding UTF8
$gameBuild = if ($agents -match 'Steam Build ID\*\*:\s*`(?<b>\d+)`') { "Steam Build ID ``$($Matches['b'])``" } else { '(AGENTS.md 확인)' }

$bt = [char]96
$notes = @"
$(if ($changelogBody) { $changelogBody } else { "$tag" })

## 설치
1. [MelonLoader](https://github.com/LavaGang/MelonLoader) 0.7.x (Il2Cpp)를 게임에 설치
2. zip 안의 ${bt}STARGAZER custom chart.dll${bt}을 ${bt}{게임 폴더}\Mods\${bt}에 복사 (게임 실행 중에는 DLL이 잠기니 종료 후 복사)
3. 게임을 한 번 실행하면 ${bt}hwa/${bt}와 ${bt}savecustomkey/config.txt${bt}가 생성됨. 곡 만드는 법: [GUIDE_CUSTOM_ALBUM.md](https://github.com/magjangin/STARGAZER-custom-chart/blob/main/docs/GUIDE_CUSTOM_ALBUM.md)

검증 대상 게임 빌드: $gameBuild (MelonLoader 0.7.3, Unity 2022.3.50f1)

## 릴리즈 스크립트 검증
- Release 빌드 성공, 단위 테스트 통과, 모드 본체 net6 검사 통과
- SHA256 (${bt}$zipName${bt}): ${bt}$zipHash${bt}
- SHA256 (${bt}STARGAZER custom chart.dll${bt}): ${bt}$dllHash${bt}

전체 변경 내역은 [CHANGELOG.md](https://github.com/magjangin/STARGAZER-custom-chart/blob/main/CHANGELOG.md).
"@
$notesFile = Join-Path $dist "notes-$tag.md"
Write-Utf8NoBom $notesFile $notes
Write-Host "릴리즈 노트 초안: $notesFile"

if (-not $Publish) {
    Write-Host "`n게시하지 않았습니다. 태그·릴리즈까지 하려면: .\tools\release.ps1 -Publish" -ForegroundColor Yellow
    return
}

# ---------------------------------------------------------------- 5. 태그·릴리즈
Step "태그 $tag 생성·푸시"
Invoke-Native 'git tag' git @('tag', '-a', $tag, '-m', "STARGAZER custom chart $tag")
try {
    Invoke-Native 'git push tag' git @('push', 'origin', "refs/tags/$tag")
} catch {
    git tag -d $tag | Out-Null
    throw "태그 푸시 실패, 로컬 태그를 지웠습니다: $_"
}

Step 'GitHub 릴리즈 생성'
try {
    Invoke-Native 'gh release create' gh @('release', 'create', $tag, $zip, '--verify-tag', '--title', "STARGAZER Custom Chart $tag", '--notes-file', $notesFile)
} catch {
    Write-Warning "태그 $tag 는 푸시됐지만 릴리즈 생성이 실패했습니다. 원인을 고친 뒤 다음으로 다시 만드세요:"
    Write-Warning "  gh release create $tag `"$zip`" --verify-tag --title `"STARGAZER Custom Chart $tag`" --notes-file `"$notesFile`""
    throw
}
Write-Host "`n릴리즈 게시 완료: https://github.com/magjangin/STARGAZER-custom-chart/releases/tag/$tag" -ForegroundColor Green
