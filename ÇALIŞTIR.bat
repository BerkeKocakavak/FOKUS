@echo off
setlocal
cd /d "%~dp0"

echo FOKUS baslatiliyor...
dotnet run --project .\KararMotoru\KararMotoru.csproj
if errorlevel 1 pause
