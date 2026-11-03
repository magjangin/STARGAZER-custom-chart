param(
    [string]$GameDir = "H:\steam\steamapps\common\Sixtar Gate STARGAZER",
    [string]$OutputDir = "$PSScriptRoot\..\il2cpp_dump",
    [string]$DumperExe = "H:\source\repos\rea tool mods\tools\il2cppdumper\Il2CppDumper.exe"
)

$ErrorActionPreference = "Stop"
$Assembly = Join-Path $GameDir "GameAssembly.dll"
$Metadata = Join-Path $GameDir "Sixtar Gate STARGAZER_Data\il2cpp_data\Metadata\global-metadata.dat"

if (-not (Test-Path $Assembly) -or -not (Test-Path $Metadata)) {
    Write-Error "GameAssembly.dll 또는 global-metadata.dat을 찾을 수 없습니다."
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host "Running Il2CppDumper..." -ForegroundColor Cyan
cmd.exe /c "`"$DumperExe`" `"$Assembly`" `"$Metadata`" `"$OutputDir`" < NUL"
Write-Host "Dump completed: $OutputDir" -ForegroundColor Green
