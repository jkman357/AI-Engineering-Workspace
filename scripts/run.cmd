@echo off
setlocal EnableExtensions

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

pushd "%~dp0.." >nul
if not exist "logs\runtime" mkdir "logs\runtime"

for /f %%I in ('powershell.exe -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss_fff"') do set "STAMP=%%I"
if not defined STAMP set "STAMP=unknown_time"
set "LOG=logs\runtime\launch_%STAMP%.log"

> "%LOG%" echo ============================================================
>>"%LOG%" echo AI Engineering Workspace launcher
>>"%LOG%" echo Version       : v0.0.6rc08
>>"%LOG%" echo Start         : %DATE% %TIME%
>>"%LOG%" echo Configuration : %CONFIG%
>>"%LOG%" echo Project       : src\AIEngineeringWorkspace.App\AIEngineeringWorkspace.App.csproj
>>"%LOG%" echo ============================================================
>>"%LOG%" echo.

dotnet run --project "src\AIEngineeringWorkspace.App\AIEngineeringWorkspace.App.csproj" -c "%CONFIG%" >>"%LOG%" 2>&1
set "RC=%ERRORLEVEL%"

>>"%LOG%" echo.
>>"%LOG%" echo End           : %DATE% %TIME%
>>"%LOG%" echo ExitCode      : %RC%
>>"%LOG%" echo LauncherLog   : %CD%\%LOG%

echo.
echo Launch exit code: %RC%
echo Launcher log   : %CD%\%LOG%

popd >nul
exit /b %RC%
