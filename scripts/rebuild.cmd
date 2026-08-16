@echo off
setlocal
call "%~dp0clean.cmd" %*
if errorlevel 1 exit /b %ERRORLEVEL%
call "%~dp0build.cmd" %*
exit /b %ERRORLEVEL%
