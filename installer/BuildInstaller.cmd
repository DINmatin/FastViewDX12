@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%.."
set "VERSION=1.2.0"
set "PAYLOAD_DIR=%SCRIPT_DIR%Payload"
set "RELEASE_DIR=%REPO_ROOT%\artifacts\FastView-%VERSION%-win-x64"
set "ISS_FILE=%SCRIPT_DIR%FastView.iss"

echo.
echo [1/4] Building the FastView release from source ...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%REPO_ROOT%\build\BuildRelease.ps1" -Version "%VERSION%"
if errorlevel 1 goto :fail

if not exist "%RELEASE_DIR%\FastViewDX12.exe" (
    echo ERROR: Release payload was not found: %RELEASE_DIR%
    goto :fail
)

echo [2/4] Refreshing installer payload ...
if exist "%PAYLOAD_DIR%" rmdir /S /Q "%PAYLOAD_DIR%"
mkdir "%PAYLOAD_DIR%" || goto :fail

robocopy "%RELEASE_DIR%" "%PAYLOAD_DIR%" /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
set "ROBOCOPY_CODE=%ERRORLEVEL%"
if %ROBOCOPY_CODE% GEQ 8 (
    echo ERROR: Robocopy failed with exit code %ROBOCOPY_CODE%.
    goto :fail
)

echo [3/4] Locating the Inno Setup compiler ...
set "ISCC="

for %%I in (
    "%ProgramFiles%\Inno Setup 7\ISCC.exe"
    "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
    "%LocalAppData%\Programs\Inno Setup 7\ISCC.exe"
    "%ProgramFiles%\Inno Setup 6\ISCC.exe"
    "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
    "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
) do (
    if not defined ISCC if exist "%%~I" set "ISCC=%%~I"
)

if not defined ISCC (
    echo ERROR: ISCC.exe was not found.
    echo Install Inno Setup 6.3 or later.
    goto :fail
)

echo Found: %ISCC%
echo [4/4] Compiling setup ...
"%ISCC%" /DMyAppVersion=%VERSION% "%ISS_FILE%"
if errorlevel 1 goto :fail

echo.
echo ================================================================
echo COMPLETE
for %%F in ("%SCRIPT_DIR%Output\FastView_Setup_*.exe") do echo Installer: %%~fF
echo ================================================================
echo.
pause
exit /B 0

:fail
echo.
echo The installer was NOT created.
echo.
pause
exit /B 1
