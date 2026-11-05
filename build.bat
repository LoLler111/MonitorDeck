@echo off
rem Compila Monitor Deck con il compilatore C# gia' presente in Windows (.NET Framework 4.x).
rem Uso: build.bat            (doppio clic)
rem      build.bat nopause    (per script e GitHub Actions)
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Compilatore C# di Windows non trovato.
  if /i not "%~1"=="nopause" pause
  exit /b 1
)
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:MonitorDeck.exe /win32icon:app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo Compilazione fallita.
  if /i not "%~1"=="nopause" pause
  exit /b 1
)
echo.
echo Fatto: MonitorDeck.exe creato.
if /i not "%~1"=="nopause" pause
exit /b 0
