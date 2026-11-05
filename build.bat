@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Compilatore C# di Windows non trovato.
  pause
  exit /b 1
)
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:MonitorDeck.exe /win32icon:app.ico /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll MonitorDeck.cs
if errorlevel 1 (
  echo.
  echo Compilazione fallita: copia gli errori qui sopra e mandameli.
  pause
  exit /b 1
)
echo.
echo Fatto: MonitorDeck.exe creato.
pause