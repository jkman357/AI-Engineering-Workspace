@echo off
call "%~dp0scripts\build.cmd" %*
exit /b %ERRORLEVEL%
