@echo off
setlocal EnableExtensions

set "ROOT_DIR=%~dp0"
set "ROOT_DIR=%ROOT_DIR:~0,-1%"
set "VERSION=2.3.1.40"
set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
set "ISS_FILE=%ROOT_DIR%\NovaFiches_2.3.0.100.iss"

echo ==================================
echo   NOVA-FICHES INSTALL BUILDER
echo   Version : %VERSION%
echo ==================================

pushd "%ROOT_DIR%\src\NovaFiches"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\build.ps1" -Configuration Release -Version "%VERSION%"
if errorlevel 1 (
  popd
  exit /b 1
)
popd

if not exist "%ROOT_DIR%\Installer\staging\publish\NovaFiches.exe" exit /b 1
if not exist "%ISCC%" exit /b 1
if not exist "%ISS_FILE%" exit /b 1

"%ISCC%" "/DMyAppVersion=%VERSION%" "%ISS_FILE%"
if errorlevel 1 exit /b 1

echo.
echo Installateur genere :
echo %ROOT_DIR%\Installer\out\NovaFiches_Setup_%VERSION%.exe
