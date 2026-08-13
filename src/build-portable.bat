@echo off
SETLOCAL

REM ---------------------------------------------------------------------------
REM Wraps the published build in a 7-Zip self-extracting exe.
REM
REM Note: this is now largely redundant. `dotnet publish
REM -p:PublishProfile=win-x64` already produces a self-contained, single-file
REM SystemOptimizer.exe that needs no .NET installed. The only thing this script
REM still adds is folding the loose TrayIcons\ folder in beside it; once the
REM tray icon is drawn at runtime (see docs\2.0-PLAN.md) the publish output is
REM one file and this script can go.
REM
REM Run `dotnet publish` FIRST - BUILD_DIR is the publish folder, not bin\Release,
REM which since the SDK-style migration is bin\Release\net8.0-windows\win-x64\.
REM ---------------------------------------------------------------------------

REM --- Paths (adjust if your layout differs) ---
REM 7-Zip moved to build-tools\ when Tools\ became the source folder for optional
REM features. Windows treats "tools" and "Tools" as the same directory, so build
REM binaries and application code cannot share the name.
set "BUILD_DIR=.\bin\publish\win-x64"
set "TOOLS_DIR=.\build-tools"
set "OUTPUT=SystemOptimizer-Portable.exe"
set "ARCHIVE=Release.7z"
set "CONFIG=sfx-config.txt"

if not exist "%BUILD_DIR%\SystemOptimizer.exe" (
  echo ERROR: Release build not found in %BUILD_DIR%
  exit /b 1
)

REM 1) Package all Release outputs into a single .7z
echo Creating 7z archive...
"%TOOLS_DIR%\7z.exe" a -r "%ARCHIVE%" "%BUILD_DIR%\*"

REM 2) Prepend the SFX stub + config + archive
echo Building self-extracting EXE...
copy /b "%TOOLS_DIR%\7z.sfx" + "%CONFIG%" + "%ARCHIVE%" "%OUTPUT%" >nul

if exist "%OUTPUT%" (
  echo SUCCESS: Created %OUTPUT%
  del "%ARCHIVE%"
) else (
  echo FAILURE: %OUTPUT% not produced
  exit /b 1
)

ENDLOCAL
