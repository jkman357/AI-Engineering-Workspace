@echo off
setlocal
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"
pushd "%~dp0.."
dotnet clean "AI-Engineering-Workspace.sln" -c "%CONFIG%" --nologo
set "RC=%ERRORLEVEL%"
popd
exit /b %RC%
