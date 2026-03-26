@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "APP_DIR=%SCRIPT_DIR%.."

if exist "%APP_DIR%\QuillForge.Web.exe" (
    set "EXEC=%APP_DIR%\QuillForge.Web.exe"
) else if exist "%SCRIPT_DIR%QuillForge.Web.exe" (
    set "EXEC=%SCRIPT_DIR%QuillForge.Web.exe"
) else (
    echo QuillForge executable not found.
    exit /b 1
)

echo Starting QuillForge...
if not defined CONTENT_ROOT set "CONTENT_ROOT=%APP_DIR%\build"
echo Content directory: %CONTENT_ROOT%

"%EXEC%" --ContentRoot "%CONTENT_ROOT%" %*
