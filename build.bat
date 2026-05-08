@echo off
setlocal enabledelayedexpansion

echo.
echo ========================================
echo.
echo STARGAZER custom chart Modding Build Script
echo.
echo ========================================
echo.

set "PROJECT_NAME=STARGAZER custom chart"
set "PROJECT_DIR=STARGAZER custom chart"
set "SOLUTION_FILE=%PROJECT_DIR%\STARGAZER custom chart.csproj"
set "DLL_NAME=%PROJECT_NAME%.dll"
set "DEFAULT_GAME_PATH=H:\steam\steamapps\common\Sixtar Gate STARGAZER"

:: Find MSBuild
set "MSBUILD_PATH="
for %%v in (18 2026 2022) do (
    for %%e in (Community Professional Enterprise BuildTools) do (
        if exist "C:\Program Files\Microsoft Visual Studio\%%v\%%e\MSBuild\Current\Bin\MSBuild.exe" (
            set "MSBUILD_PATH=C:\Program Files\Microsoft Visual Studio\%%v\%%e\MSBuild\Current\Bin\MSBuild.exe"
            goto :found
        )
    )
)
if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
)

:found
if "!MSBUILD_PATH!"=="" (
    echo [ERROR] MSBuild not found.
    echo [ERROR] Please check if Visual Studio is installed.
    pause
    exit /b 1
)

echo [INFO] MSBuild path: !MSBUILD_PATH!
echo.

:: Get script directory
set "SCRIPT_DIR=%~dp0"
set "SOURCE_ROOT=!SCRIPT_DIR!"
set "SOLUTION_PATH=!SCRIPT_DIR!!SOLUTION_FILE!"
set "SOURCE_DLL=!SCRIPT_DIR!!PROJECT_DIR!\bin\Debug\net6.0\%DLL_NAME%"

if not defined GAME_PATH (
    if exist "!DEFAULT_GAME_PATH!" (
        set "GAME_PATH=!DEFAULT_GAME_PATH!"
    )
)

set "MODS_DIR=!GAME_PATH!\Mods"
set "TARGET_DLL=!MODS_DIR!\%DLL_NAME%"

echo [INFO] Starting Debug build...
echo [INFO] GamePath: !GAME_PATH!
echo.

:: Avoid Roslyn compiler server hash/version mismatch issues.
taskkill /IM VBCSCompiler.exe /F >nul 2>&1

:: Restore NuGet packages
echo [INFO] Restoring NuGet packages...
"!MSBUILD_PATH!" "!SOLUTION_PATH!" /p:Configuration=Debug /p:Platform="AnyCPU" /p:GamePath="!GAME_PATH!" /p:UseSharedCompilation=false /nr:false /t:Restore /v:minimal /nologo

:: Build project
echo [INFO] Building project...
"!MSBUILD_PATH!" "!SOLUTION_PATH!" /p:Configuration=Debug /p:Platform="AnyCPU" /p:GamePath="!GAME_PATH!" /p:UseSharedCompilation=false /nr:false /t:Build /v:minimal /nologo

if errorlevel 1 (
    echo.
    echo ========================================
    echo [ERROR] Build failed
    echo ========================================
    pause
    exit /b 1
)

echo.
echo ========================================
echo [SUCCESS] Build completed
echo ========================================
echo.

:: Verify DLL file
if not exist "!SOURCE_DLL!" (
    echo [ERROR] DLL file not found: !SOURCE_DLL!
    pause
    exit /b 1
)

for %%F in ("!SOURCE_DLL!") do (
    set "FILE_SIZE=%%~zF"
    set "FILE_TIME=%%~tF"
)

echo [INFO] Built DLL file: !SOURCE_DLL!
echo [INFO] File size: !FILE_SIZE! bytes
echo [INFO] Modified time: !FILE_TIME!
echo.

if !FILE_SIZE! LSS 1024 (
    echo [ERROR] DLL file size is too small: !FILE_SIZE! bytes
    pause
    exit /b 1
)

:: Copy to Mods directory
echo ========================================
echo [STEP] Copying DLL to Mods directory...
echo ========================================
echo.

if not exist "!GAME_PATH!" (
    echo [ERROR] Game directory not found: !GAME_PATH!
    pause
    exit /b 1
)

if not exist "!MODS_DIR!" (
    echo [INFO] Creating Mods directory...
    mkdir "!MODS_DIR!"
)

echo [INFO] Copying !SOURCE_DLL!
echo [INFO]      to !TARGET_DLL!
echo.

copy /Y "!SOURCE_DLL!" "!TARGET_DLL!" >nul

if errorlevel 1 (
    echo [ERROR] File copy failed
    pause
    exit /b 1
)

:: Verify copied file
for %%F in ("!TARGET_DLL!") do set "COPIED_SIZE=%%~zF"

if not "!FILE_SIZE!"=="!COPIED_SIZE!" (
    echo [ERROR] File sizes do not match!
    echo [ERROR] Source: !FILE_SIZE! bytes
    echo [ERROR] Copied: !COPIED_SIZE! bytes
    pause
    exit /b 1
)

echo ========================================
echo [SUCCESS] DLL copied successfully
echo ========================================
echo.
echo [INFO]  Source: !SOURCE_DLL!
echo [INFO]  Target: !TARGET_DLL!
echo [INFO]  File size: !COPIED_SIZE! bytes
echo.

pause
