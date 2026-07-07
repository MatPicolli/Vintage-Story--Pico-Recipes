@echo off
setlocal enableextensions enabledelayedexpansion

REM ============================================================================
REM  Universal Vintage Story mod build & package script
REM
REM  Drop this file in the root of any code-mod repo and run it. It:
REM    1. Finds modinfo.json and the .csproj automatically (ignoring bin/obj)
REM    2. Reads the modid + current version from modinfo.json
REM    3. Asks for the version (press Enter to keep the current one)
REM    4. Writes that version into modinfo.json
REM    5. Builds the project in Release with dotnet
REM    6. Packages modinfo.json + assets\ + the built DLL(s) into
REM       Releases\<modid>_<version>.zip, ready to drop into VS's Mods folder
REM
REM  Usage:  build-mod.bat            (prompts for the version)
REM          build-mod.bat 0.2.0      (uses 0.2.0 without prompting)
REM
REM  Requires: the .NET SDK (dotnet) on PATH, and (for code mods) the
REM  VINTAGE_STORY environment variable pointing at your game folder.
REM ============================================================================

cd /d "%~dp0"

set "CONFIG=Release"
set "RELEASEDIR=Releases"

REM ---- 1. Locate the source modinfo.json (first match outside bin/obj) ----
set "MODINFO="
for /f "delims=" %%f in ('dir /b /s modinfo.json 2^>nul ^| findstr /i /v /c:"\bin\" /c:"\obj\"') do (
    if not defined MODINFO set "MODINFO=%%f"
)
if not defined MODINFO (
    echo [ERROR] No modinfo.json found. Run this from a Vintage Story mod folder.
    goto :fail
)
for %%f in ("%MODINFO%") do set "MODDIR=%%~dpf"
if "%MODDIR:~-1%"=="\" set "MODDIR=%MODDIR:~0,-1%"

REM ---- 2. Read modid + current version (regex, so formatting is preserved) ----
set "MODID="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "$q=[char]34; $c=[IO.File]::ReadAllText('%MODINFO%'); if($c -match ($q+'modid'+$q+'\s*:\s*'+$q+'([^'+$q+']*)'+$q)){$Matches[1]}"`) do set "MODID=%%v"
set "CURVER="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "$q=[char]34; $c=[IO.File]::ReadAllText('%MODINFO%'); if($c -match ($q+'version'+$q+'\s*:\s*'+$q+'([^'+$q+']*)'+$q)){$Matches[1]}"`) do set "CURVER=%%v"

if not defined MODID (
    echo [ERROR] Could not read "modid" from %MODINFO%.
    goto :fail
)

REM ---- 3. Determine the version: argument, else prompt (default = current) ----
set "VERSION=%~1"
if not defined VERSION (
    set "VERSION="
    set /p "VERSION=Enter mod version [%CURVER%]: "
)
if not defined VERSION set "VERSION=%CURVER%"
if not defined VERSION (
    echo [ERROR] No version given and none found in modinfo.json.
    goto :fail
)

echo.
echo === Building %MODID% version %VERSION% ===
echo.

REM ---- 4. Stamp the version into modinfo.json (only the "version" field) ----
powershell -NoProfile -Command "$q=[char]34; $p='%MODINFO%'; $c=[IO.File]::ReadAllText($p); $c=[regex]::Replace($c, ($q+'version'+$q+'\s*:\s*'+$q+'[^'+$q+']*'+$q), ($q+'version'+$q+': '+$q+'%VERSION%'+$q)); [IO.File]::WriteAllText($p, $c, (New-Object Text.UTF8Encoding($false)))"
if errorlevel 1 (
    echo [ERROR] Failed to update the version in modinfo.json.
    goto :fail
)

REM ---- 5. Locate the .csproj to build (first match outside bin/obj) ----
set "PROJECT="
for /f "delims=" %%f in ('dir /b /s *.csproj 2^>nul ^| findstr /i /v /c:"\bin\" /c:"\obj\"') do (
    if not defined PROJECT set "PROJECT=%%f"
)
if not defined PROJECT (
    echo [ERROR] No .csproj found to build.
    goto :fail
)
for %%f in ("%PROJECT%") do set "PROJNAME=%%~nf"
for %%f in ("%PROJECT%") do set "PROJDIR=%%~dpf"
if "%PROJDIR:~-1%"=="\" set "PROJDIR=%PROJDIR:~0,-1%"

REM ---- 6. Build ----
dotnet build "%PROJECT%" -c %CONFIG%
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. See the messages above.
    goto :fail
)

REM ---- 7. Find the built DLL (its folder is the mod's compiled output) ----
REM Prefer <project>.dll; fall back to the newest .dll under bin\<Config>.
set "DLL="
for /f "delims=" %%f in ('dir /b /s "%PROJDIR%\bin\%CONFIG%\%PROJNAME%.dll" 2^>nul') do set "DLL=%%f"
if not defined DLL (
    for /f "delims=" %%f in ('dir /b /s /o:d "%PROJDIR%\bin\%CONFIG%\*.dll" 2^>nul') do set "DLL=%%f"
)
if not defined DLL (
    echo.
    echo [ERROR] Build reported success but no output .dll was found under
    echo         %PROJDIR%\bin\%CONFIG%\
    goto :fail
)
for %%f in ("%DLL%") do set "DLLDIR=%%~dpf"
if "%DLLDIR:~-1%"=="\" set "DLLDIR=%DLLDIR:~0,-1%"

echo Built: %DLL%

REM ---- 8. Stage the mod contents and zip them ----
if not exist "%RELEASEDIR%" mkdir "%RELEASEDIR%"
set "STAGE=%RELEASEDIR%\_pack_%MODID%"
if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%STAGE%"

REM modinfo.json at the zip root (freshly stamped source copy)
copy /y "%MODINFO%" "%STAGE%\modinfo.json" >nul

REM assets\ folder, if the mod has one (VS convention: sibling of modinfo.json)
if exist "%MODDIR%\assets" xcopy /e /i /y /q "%MODDIR%\assets" "%STAGE%\assets" >nul

REM the compiled DLL(s) from the build output (VS references aren't copied there)
copy /y "%DLLDIR%\*.dll" "%STAGE%\" >nul

set "ZIP=%RELEASEDIR%\%MODID%_%VERSION%.zip"
if exist "%ZIP%" del "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
    echo.
    echo [ERROR] Packaging into the zip failed.
    goto :fail
)

rmdir /s /q "%STAGE%"

echo.
echo === Done ===
echo   Mod id : %MODID%
echo   Version: %VERSION%
echo   Zip    : %ZIP%
echo.
pause
exit /b 0

:fail
echo.
pause
exit /b 1
