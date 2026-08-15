@echo off
setlocal EnableExtensions

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

pushd "%~dp0.." >nul
if not exist "logs\build" mkdir "logs\build"

for /f %%I in ('powershell.exe -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss_fff"') do set "STAMP=%%I"
if not defined STAMP set "STAMP=unknown_time"
set "LOG=logs\build\build_%STAMP%.log"

> "%LOG%" echo ============================================================
>>"%LOG%" echo AI Engineering Workspace build
>>"%LOG%" echo Version       : v0.0.6rc03
>>"%LOG%" echo Start         : %DATE% %TIME%
>>"%LOG%" echo Configuration : %CONFIG%
>>"%LOG%" echo Solution      : AI-Engineering-Workspace.sln
>>"%LOG%" echo Computer      : %COMPUTERNAME%
>>"%LOG%" echo User          : %USERNAME%
>>"%LOG%" echo ============================================================
>>"%LOG%" echo.
>>"%LOG%" echo --- dotnet --info ---
dotnet --info >>"%LOG%" 2>&1
set "INFO_RC=%ERRORLEVEL%"

if not "%INFO_RC%"=="0" (
    >>"%LOG%" echo.
    >>"%LOG%" echo ERROR: dotnet --info failed. Ensure the .NET 10 SDK is installed and available on PATH.
    set "RC=%INFO_RC%"
    goto :finish
)

>>"%LOG%" echo.
>>"%LOG%" echo --- dotnet build ---
dotnet build "AI-Engineering-Workspace.sln" -c "%CONFIG%" --nologo >>"%LOG%" 2>&1
set "RC=%ERRORLEVEL%"

:finish
>>"%LOG%" echo.
>>"%LOG%" echo End           : %DATE% %TIME%
>>"%LOG%" echo ExitCode      : %RC%
>>"%LOG%" echo Log           : %CD%\%LOG%

echo.
echo Build exit code: %RC%
echo Build log      : %CD%\%LOG%
echo.
type "%LOG%"

popd >nul
exit /b %RC%
