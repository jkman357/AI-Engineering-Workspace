@echo off
call "%~dp0scripts\rebuild.cmd" %*
exit /b %ERRORLEVEL%
