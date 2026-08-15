@echo off
setlocal
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"
call "%~dp0clean.cmd" "%CONFIG%"
if errorlevel 1 exit /b %ERRORLEVEL%
call "%~dp0build.cmd" "%CONFIG%"
exit /b %ERRORLEVEL%
