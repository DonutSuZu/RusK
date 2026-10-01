@echo off
rem Build a release: RusK Mod Manager (dist\RusK-Mod-Manager.exe) + RusK core (dist\RusK-Core.zip)
rem (ASCII-only: cmd misparses UTF-8 multibyte text in .bat files)
setlocal
chcp 65001 > nul
cd /d "%~dp0"

set "GAMEDIR="
for /d %%D in ("Y:\SteamLibrary\steamapps\common\Ved*") do (
    if exist "%%~fD\ved.exe" set "GAMEDIR=%%~fD"
)
if not defined GAMEDIR (
    echo [ERROR] Game folder not found. The build needs the game's BepInEx\interop assemblies.
    goto :fail
)

echo GameDir: %GAMEDIR%
dotnet build "RusK.Manager\RusK.Manager.csproj" -c Release -p:GameDir="%GAMEDIR%"
if errorlevel 1 goto :fail

echo.
echo [OK] Mod Manager and RusK core are in dist\
dir /b dist\RusK-Mod-Manager.exe dist\RusK-Core.zip
pause
exit /b 0

:fail
echo.
echo [FAILED]
pause
exit /b 1
