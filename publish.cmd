@echo off
rem Erzeugt eine einzelne Flarelog.exe im Ordner "publish".
rem Sie enthaelt .NET bereits und laeuft damit auch auf PCs ohne installiertes .NET.
cd /d "%~dp0"
echo Erzeuge Flarelog.exe ...
dotnet publish src\AeroflyAcars.Desktop -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
if errorlevel 1 (
  echo.
  echo Fehler beim Erzeugen - siehe Meldungen oben.
  pause
  exit /b 1
)
echo.
echo Fertig: %~dp0publish\Flarelog.exe
pause
