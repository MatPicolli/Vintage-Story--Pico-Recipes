@echo off
setlocal enabledelayedexpansion

REM ============================================================================
REM  Pico Recipes - build & package script
REM
REM  1. Asks for the mod version (press Enter to keep the current one)
REM  2. Writes that version into modinfo.json
REM  3. Builds the mod in Release with dotnet
REM  4. Zips the result into Releases\picorecipes_<version>.zip
REM     (PicoRecipes.dll + modinfo.json + assets, ready to drop into VS's Mods)
REM
REM  Usage:  build.bat            (prompts for the version)
REM          build.bat 0.2.0      (uses 0.2.0 without prompting)
REM ============================================================================

cd /d "%~dp0"

set "MODID=picorecipes"
set "CONFIG=Release"
set "PROJECT=PicoRecipes\PicoRecipes.csproj"
set "MODINFO=PicoRecipes\modinfo.json"
set "MODSROOT=PicoRecipes\bin\%CONFIG%\Mods"
set "RELEASEDIR=Releases"

REM ---- Read the current version from modinfo.json (used as the default) ----
set "CURVER="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "$q=[char]34; $c=[IO.File]::ReadAllText('%MODINFO%'); if($c -match ($q+'version'+$q+'\s*:\s*'+$q+'([^'+$q+']*)'+$q)){$Matches[1]}"`) do set "CURVER=%%v"

REM ---- Determine the version: argument, else prompt (default = current) ----
set "VERSION=%~1"
if not defined VERSION (
    set "VERSION="
    set /p "VERSION=Enter mod version [%CURVER%]: "
)
if not defined VERSION set "VERSION=%CURVER%"
if not defined VERSION (
    echo [ERROR] No version given and none found in modinfo.json. Aborting.
    exit /b 1
)

echo.
echo === Building %MODID% version %VERSION% ===
echo.

REM ---- Stamp the version into modinfo.json (only the "version" field) ----
powershell -NoProfile -Command "$q=[char]34; $p='%MODINFO%'; $c=[IO.File]::ReadAllText($p); $c=[regex]::Replace($c, ($q+'version'+$q+'\s*:\s*'+$q+'[^'+$q+']*'+$q), ($q+'version'+$q+': '+$q+'%VERSION%'+$q)); [IO.File]::WriteAllText($p, $c, (New-Object Text.UTF8Encoding($false)))"
if errorlevel 1 (
    echo [ERROR] Failed to update the version in modinfo.json.
    exit /b 1
)

REM ---- Build ----
dotnet build "%PROJECT%" -c %CONFIG%
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. See the messages above.
    exit /b 1
)

REM ---- Locate the freshly built mod DLL wherever it landed (case-insensitive) ----
REM The assembly is PicoRecipes.dll while the modid is lowercase, and the output
REM folder case can vary, so we just find the .dll under the Mods output folder
REM instead of assuming an exact path.
set "DLL="
for /f "delims=" %%f in ('dir /b /s "%MODSROOT%\*.dll" 2^>nul') do set "DLL=%%f"

if not defined DLL (
    echo.
    echo [ERROR] Build reported success but no mod .dll was found under
    echo         %MODSROOT%\
    exit /b 1
)

REM The mod folder to zip is the directory that contains the DLL.
for %%f in ("%DLL%") do set "OUTDIR=%%~dpf"
if "%OUTDIR:~-1%"=="\" set "OUTDIR=%OUTDIR:~0,-1%"

echo Built: %DLL%

REM ---- Package the built mod folder into a zip ----
if not exist "%RELEASEDIR%" mkdir "%RELEASEDIR%"
set "ZIP=%RELEASEDIR%\%MODID%_%VERSION%.zip"
if exist "%ZIP%" del "%ZIP%"

powershell -NoProfile -Command "Compress-Archive -Path '%OUTDIR%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
    echo.
    echo [ERROR] Packaging into the zip failed.
    exit /b 1
)

echo.
echo === Done ===
echo   Mod folder : %OUTDIR%
echo   Release zip: %ZIP%
echo.

pause
endlocal
