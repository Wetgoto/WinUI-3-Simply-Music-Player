@echo off
setlocal
set "DOTNET_BUNDLE_EXTRACT_BASE_DIR=%~dp0.runtime"
if not exist "%~dp0.runtime" mkdir "%~dp0.runtime"
attrib +h "%~dp0.runtime" >nul 2>&1
start "" "%~dp0Music.exe"
endlocal
