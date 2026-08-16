@echo off
setlocal EnableExtensions
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"
pushd "%~dp0.." >nul
set "VERSION=unknown"
for /f "tokens=2 delims=<>" %%V in ('findstr /c:"<WorkspaceVersionLabel>" "Directory.Build.props"') do set "VERSION=%%V"
if not exist "logs\runtime" mkdir "logs\runtime"
for /f %%I in ('powershell.exe -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss_fff"') do set "STAMP=%%I"
if not defined STAMP set "STAMP=unknown_time"
set "LOG=logs\runtime\launch_%STAMP%.log"
> "%LOG%" echo AI Engineering Workspace launcher - %VERSION%
dotnet run --project "src\AIEngineeringWorkspace.App\AIEngineeringWorkspace.App.csproj" -c "%CONFIG%" >>"%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
echo Launch exit code: %RC%
echo Launcher log   : %CD%\%LOG%
popd >nul
exit /b %RC%
