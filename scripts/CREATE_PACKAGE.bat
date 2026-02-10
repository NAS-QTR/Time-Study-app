@echo off
REM Quick Package Builder for Video Time Study
REM Double-click this file to create a shareable package

echo.
echo ========================================
echo Video Time Study - Package Builder
echo ========================================
echo.
echo Creating shareable package...
echo This will take a few minutes...
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0package.ps1"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Package created successfully!
    echo Check the Releases folder for the ZIP file.
    echo.
    pause
) else (
    echo.
    echo ERROR: Build failed!
    echo.
    pause
)
