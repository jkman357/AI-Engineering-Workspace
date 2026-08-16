@echo off
setlocal EnableExtensions
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"
pushd "%~dp0.." >nul
set "VERSION=unknown"
for /f "tokens=2 delims=<>" %%V in ('findstr /c:"<WorkspaceVersionLabel>" "Directory.Build.props"') do set "VERSION=%%V"
if not exist "logs\build" mkdir "logs\build"
for /f %%I in ('powershell.exe -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss_fff"') do set "STAMP=%%I"
if not defined STAMP set "STAMP=unknown_time"
set "LOG=logs\build\build_%STAMP%.log"
> "%LOG%" echo ============================================================
>>"%LOG%" echo AI Engineering Workspace build
>>"%LOG%" echo Version       : %VERSION%
>>"%LOG%" echo Start         : %DATE% %TIME%
>>"%LOG%" echo Configuration : %CONFIG%
>>"%LOG%" echo Solution      : AI-Engineering-Workspace.sln
>>"%LOG%" echo ============================================================
dotnet --info >>"%LOG%" 2>&1
if errorlevel 1 (set "RC=%ERRORLEVEL%"&goto :finish)
dotnet build "AI-Engineering-Workspace.sln" -c "%CONFIG%" --nologo >>"%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
:finish
>>"%LOG%" echo ExitCode      : %RC%
echo Build exit code: %RC%
echo Build log      : %CD%\%LOG%
type "%LOG%"
popd >nul
exit /b %RC%
