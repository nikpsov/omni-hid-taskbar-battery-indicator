@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo   Building OmniHID Taskbar Battery Indicator
echo ===================================================

set CSC_PATH=
set WPF_PATH=
for /d %%D in ("%WINDIR%\Microsoft.NET\Framework64\v4.0.30319", "%WINDIR%\Microsoft.NET\Framework\v4.0.30319") do (
    if exist "%%~D\csc.exe" (
        set "CSC_PATH=%%~D\csc.exe"
        if exist "%%~D\WPF" set "WPF_PATH=%%~D\WPF"
    )
)

if "%CSC_PATH%"=="" (
    echo [ERROR] csc.exe not found!
    exit /b 1
)

if not exist "bin" mkdir bin

echo.
echo [1/3] Compiling OmniHid.Core.dll from submodule (embedding device profiles)...
set "RESOURCES="
for /r "vendor\omni-hid\devices" %%f in (*.json) do (
    set "RESOURCES=!RESOURCES! /resource:"%%f",%%~nxf"
)
"%CSC_PATH%" /nologo /target:library /optimize+ /out:bin\OmniHid.Core.dll ^
    /reference:System.Windows.Forms.dll ^
    /recurse:vendor\omni-hid\src\OmniHid.Core\*.cs !RESOURCES!

if errorlevel 1 (
    echo [ERROR] OmniHid.Core compilation failed.
    exit /b 1
)

set REFS=/reference:PresentationFramework.dll,PresentationCore.dll,WindowsBase.dll,System.Xaml.dll,System.Drawing.dll,System.Windows.Forms.dll,bin\OmniHid.Core.dll
if not "%WPF_PATH%"=="" set REFS=/lib:"%WPF_PATH%",bin %REFS%
set SOURCES=src\Core\*.cs src\UI\*.cs src\App.cs

echo.
echo [2/3] Compiling OmniHidTaskbar.exe (Release Windowless)...
"%CSC_PATH%" /nologo /target:winexe /optimize+ /out:bin\OmniHidTaskbar.exe %REFS% %SOURCES%
if errorlevel 1 (
    echo [ERROR] OmniHidTaskbar compilation failed.
    exit /b 1
)

echo.
echo [3/3] Compiling OmniHidTaskbarDebug.exe (Debug Console)...
"%CSC_PATH%" /nologo /target:exe /optimize+ /define:DEBUG_LOG /out:bin\OmniHidTaskbarDebug.exe %REFS% %SOURCES%
if errorlevel 1 (
    echo [ERROR] OmniHidTaskbarDebug compilation failed.
    exit /b 1
)

if not exist "bin\settings.json" copy "settings.json" "bin\settings.json" >nul

echo.
echo [SUCCESS] Build succeeded!
echo   - bin\OmniHid.Core.dll
echo   - bin\OmniHidTaskbar.exe (Portable Release)
echo   - bin\OmniHidTaskbarDebug.exe (Debug Console)
exit /b 0