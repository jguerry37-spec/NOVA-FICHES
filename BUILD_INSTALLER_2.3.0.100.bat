@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ------------------------------------------------------------
REM NOVATLAS - Build installeur Nova-Fiches 2.3.0.100
REM A placer et lancer depuis :
REM C:\Users\micro\Downloads\00 - DEV\Nova-Fiches_2.3.0.100_FIX17_UI_LEVE_XY_DISPLAY
REM ------------------------------------------------------------

set "ROOT_DIR=%~dp0"
set "ROOT_DIR=%ROOT_DIR:~0,-1%"
set "VERSION=2.3.0.100"

echo ===============================
echo   NOVA-FICHES INSTALL BUILDER
echo   Version : %VERSION%
echo ===============================
echo.
echo Dossier projet :
echo %ROOT_DIR%
echo.

REM --- Purge cache WebView2 ---
echo == Purge cache WebView2 ==
powershell -NoProfile -ExecutionPolicy Bypass -Command "Remove-Item \"$env:LOCALAPPDATA\NOVATLAS\Nova-Fiches\WebView2\" -Recurse -Force -ErrorAction SilentlyContinue"

REM --- Build / publish ---
echo.
echo == Publish Nova-Fiches ==
if not exist "%ROOT_DIR%\src\NovaFiches\build.ps1" (
    echo ERREUR : build.ps1 introuvable :
    echo "%ROOT_DIR%\src\NovaFiches\build.ps1"
    REM pause
    exit /b 1
)

pushd "%ROOT_DIR%\src\NovaFiches"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\build.ps1" -Configuration Release
if errorlevel 1 (
    echo.
    echo ECHEC : build.ps1 / dotnet publish a echoue.
    popd
    REM pause
    exit /b 1
)
popd

REM --- V‚rification publish ---
set "PUBLISH_DIR=%ROOT_DIR%\Installer\staging\publish"
set "EXE_PATH=%PUBLISH_DIR%\NovaFiches.exe"

if not exist "%EXE_PATH%" (
    echo.
    echo ERREUR : NovaFiches.exe introuvable apres publish :
    echo "%EXE_PATH%"
    REM pause
    exit /b 1
)

REM --- Inno Setup ---
set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
    echo.
    echo ERREUR : ISCC.exe introuvable :
    echo "%ISCC%"
    echo Installe Inno Setup 6 ou adapte le chemin dans ce .bat.
    REM pause
    exit /b 1
)

set "ISS_FILE=%ROOT_DIR%\NovaFiches_2.3.0.100.iss"
if not exist "%ISS_FILE%" (
    echo.
    echo ERREUR : fichier .iss introuvable :
    echo "%ISS_FILE%"
    REM pause
    exit /b 1
)

echo.
echo == Compilation Inno Setup ==
"%ISCC%" "/DMyAppVersion=%VERSION%" "%ISS_FILE%"
if errorlevel 1 (
    echo.
    echo ECHEC compilation Inno Setup.
    REM pause
    exit /b 1
)

echo.
echo ===============================
echo   INSTALLER GENERE
echo   Version : %VERSION%
echo   Sortie  : %ROOT_DIR%\Installer\out
echo ===============================
REM pause

