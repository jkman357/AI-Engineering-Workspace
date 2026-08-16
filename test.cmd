@echo off
call "%~dp0scripts\test.cmd" %*
exit /b %ERRORLEVEL%
