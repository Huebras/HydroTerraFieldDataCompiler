@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK was not found.
  echo Install Visual Studio 2022 with the .NET desktop development workload.
  pause
  exit /b 1
)

echo Restoring project...
dotnet restore HydroTerraFieldDataCompiler.sln
if errorlevel 1 goto :fail

echo Building Release version...
dotnet build HydroTerraFieldDataCompiler.sln -c Release --no-restore
if errorlevel 1 goto :fail

if exist bin rmdir /s /q bin
mkdir bin
xcopy /e /i /y "src\HydroTerraFieldDataCompiler\bin\Release\net8.0-windows\*" "bin\" >nul

echo.
echo Build succeeded.
echo All required runtime files were copied to the bin folder.
echo Run: bin\HydroTerraFieldDataCompiler.exe
echo.
pause
exit /b 0

:fail
echo.
echo Build failed. Review the first error above.
pause
exit /b 1
