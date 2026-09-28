@echo off
rem Build the whole RusK solution and deploy to the Steam game folder on Y:
rem (ASCII-only: cmd misparses UTF-8 multibyte text in .bat files)
setlocal
chcp 65001 > nul
cd /d "%~dp0"

set "GAMEDIR="
for /d %%D in ("Y:\SteamLibrary\steamapps\common\Ved*") do (
    if exist "%%~fD\ved.exe" set "GAMEDIR=%%~fD"
)

if not defined GAMEDIR (
    echo [ERROR] Game folder not found under Y:\SteamLibrary\steamapps\common
    goto :fail
)

if not exist "%GAMEDIR%\BepInEx\interop\Assembly-CSharp.dll" (
    echo [ERROR] BepInEx interop assemblies missing. Launch the game once with BepInEx.
    goto :fail
)

tasklist /fi "imagename eq ved.exe" 2>nul | find /i "ved.exe" > nul
if not errorlevel 1 (
    echo [ERROR] ved.exe is running. Close the game first.
    goto :fail
)

echo GameDir: %GAMEDIR%
dotnet build RusK.sln -c Release -p:GameDir="%GAMEDIR%"
if errorlevel 1 goto :fail

echo.
echo [OK] RusK.Core  -^> BepInEx\plugins\RusK
echo [OK] Mods       -^> RusK\mods
pause
exit /b 0

:fail
echo.
echo [FAILED]
pause
exit /b 1
