@echo off
setlocal
chcp 936 >nul

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. .NET Framework 4.x is required.
    exit /b 1
)

pushd "%~dp0"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 ^
    /win32manifest:app.manifest ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    /out:NavPaneLock.exe ^
    App.cs NavPaneCore.cs PowerCore.cs ClipboardCore.cs ContextMenuCore.cs Gui.cs

set "RC=%ERRORLEVEL%"
popd

if not "%RC%"=="0" (
    echo [ERROR] build failed, exit code %RC%
    exit /b %RC%
)

echo [OK] NavPaneLock.exe built.
endlocal
exit /b 0
