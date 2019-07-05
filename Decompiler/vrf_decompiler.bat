@echo off

WHERE dotnet >nul 2>nul
IF %ERRORLEVEL% NEQ 0 ECHO You need to install .NET Core from https://dot.net && EXIT /B 1

dotnet %~dp0Decompiler.dll %*
EXIT /B %errorlevel%
