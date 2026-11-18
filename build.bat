@echo off
setlocal enabledelayedexpansion

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

set "SCRIPT_DIR=%~dp0"
set "PROJECT_PATH=%SCRIPT_DIR%STARGAZER custom chart\STARGAZER custom chart.csproj"
set "DEFAULT_GAME_PATH=H:\steam\steamapps\common\Sixtar Gate STARGAZER"

if not defined GAME_PATH (
    if exist "%DEFAULT_GAME_PATH%" (
        set "GAME_PATH=%DEFAULT_GAME_PATH%"
    )
)

echo [INFO] Configuration: %CONFIG%
if defined GAME_PATH echo [INFO] GamePath: %GAME_PATH%

where dotnet >nul 2>&1
if %errorlevel%==0 (
    if defined GAME_PATH (
        dotnet build "%PROJECT_PATH%" -c "%CONFIG%" -p:GamePath="%GAME_PATH%"
    ) else (
        dotnet build "%PROJECT_PATH%" -c "%CONFIG%"
    )
    exit /b %errorlevel%
)

set "MSBUILD_PATH="
for %%v in (18 2026 2022 2019) do (
    for %%e in (Community Professional Enterprise BuildTools) do (
        if exist "C:\Program Files\Microsoft Visual Studio\%%v\%%e\MSBuild\Current\Bin\MSBuild.exe" (
            set "MSBUILD_PATH=C:\Program Files\Microsoft Visual Studio\%%v\%%e\MSBuild\Current\Bin\MSBuild.exe"
            goto :found
        )
    )
)

:found
if "!MSBUILD_PATH!"=="" (
    echo [ERROR] Neither dotnet nor MSBuild was found.
    exit /b 1
)

taskkill /IM VBCSCompiler.exe /F >nul 2>&1
"!MSBUILD_PATH!" "%PROJECT_PATH%" /p:Configuration="%CONFIG%" /p:Platform="AnyCPU" /p:GamePath="%GAME_PATH%" /p:UseSharedCompilation=false /nr:false /t:Restore;Build /v:minimal /nologo
exit /b %errorlevel%
