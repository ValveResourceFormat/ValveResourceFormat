@echo off

WHERE dotnet >nul 2>nul
IF %ERRORLEVEL% NEQ 0 ECHO You need to install .NET Core from https://dot.net && EXIT \b 1

dotnet Decompiler.dll %*
