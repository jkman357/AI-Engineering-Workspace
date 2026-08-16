@echo off
setlocal EnableExtensions

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

pushd "%~dp0.." >nul
if not exist "logs\test" mkdir "logs\test"

set "VERSION=unknown"
for /f "tokens=2 delims=<>" %%V in ('findstr /c:"<WorkspaceVersionLabel>" "Directory.Build.props"') do set "VERSION=%%V"
for /f %%I in ('powershell.exe -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss_fff"') do set "STAMP=%%I"
if not defined STAMP set "STAMP=unknown_time"
set "LOG=logs\test\test_%STAMP%.log"

> "%LOG%" echo ============================================================
>>"%LOG%" echo AI Engineering Workspace regression tests
>>"%LOG%" echo Version       : %VERSION%
>>"%LOG%" echo Start         : %DATE% %TIME%
>>"%LOG%" echo Configuration : %CONFIG%
>>"%LOG%" echo ============================================================
>>"%LOG%" echo.

dotnet run --project "tests\AIEngineeringWorkspace.Tests\AIEngineeringWorkspace.Tests.csproj" -c "%CONFIG%" --nologo >>"%LOG%" 2>&1
set "RC=%ERRORLEVEL%"

>>"%LOG%" echo.
>>"%LOG%" echo End           : %DATE% %TIME%
>>"%LOG%" echo ExitCode      : %RC%
>>"%LOG%" echo Log           : %CD%\%LOG%

echo.
echo Test exit code: %RC%
echo Test log      : %CD%\%LOG%
echo.
type "%LOG%"
popd >nul
exit /b %RC%
