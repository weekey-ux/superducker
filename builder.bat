@echo off
setlocal EnableDelayedExpansion

REM === SuperDucker build and publish script ===
REM Usage:
REM   builder.bat                normal publish, no obfuscation
REM   builder.bat -obfuscate     publish with Obfuscar, install first: dotnet tool install Obfuscar.GlobalTool -g
REM
REM Version uses SemVer and is NOT auto-bumped.
REM Before release, manually increment Version in the three csproj files
REM (SuperDucker.App / SuperDucker.Cli / SuperDucker.Repo must stay aligned).

set "ROOT=%~dp0"
set "APP_PROJ=%ROOT%SuperDucker.App\SuperDucker.App.csproj"
set "CLI_PROJ=%ROOT%SuperDucker.Cli\SuperDucker.Cli.csproj"
set "REPO_PROJ=%ROOT%SuperDucker.Repo\SuperDucker.Repo.csproj"
set "PUB_APP=%ROOT%publish\app"
set "PUB_CLI=%ROOT%publish\cli"
set "PUB_REPO=%ROOT%publish\repo"
set "OBF_FLAG="

REM ---- parse args ----
:parse
if "%~1"=="" goto :done_parse
if /i "%~1"=="-obfuscate" ( set "OBF_FLAG=-p:Obfuscate=true" & echo [Config] Obfuscation ENABLED )
if /i "%~1"=="/obfuscate" ( set "OBF_FLAG=-p:Obfuscate=true" & echo [Config] Obfuscation ENABLED )
shift
goto :parse
:done_parse

echo.
echo ============ SuperDucker Build ============

REM ---- Step 1: read current version from App csproj ----
set "CUR_VER=unknown"
for /f "usebackq delims=" %%v in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-Content '%APP_PROJ%' -Raw) -match '<Version>([0-9.]+)</Version>' | Out-Null; $Matches[1]"`) do set "CUR_VER=%%v"
echo Version: %CUR_VER%  (manual SemVer bump required before release)
echo.

REM ---- Step 2: precompile (Release) sub-projects ----
echo [1/4] Building SuperDucker.Shared ...
dotnet build "%ROOT%SuperDucker.Shared\SuperDucker.Shared.csproj" -c Release
if errorlevel 1 goto :fail

echo [2/4] Building SuperDucker.App ...
dotnet build "%APP_PROJ%" -c Release
if errorlevel 1 goto :fail

echo [3/4] Building SuperDucker.Cli ...
dotnet build "%CLI_PROJ%" -c Release
if errorlevel 1 goto :fail

echo [4/4] Building SuperDucker.Repo ...
dotnet build "%REPO_PROJ%" -c Release
if errorlevel 1 goto :fail

REM ---- Step 3: publish (single-file, self-contained) to publish\, replacing old ----
echo.
echo [Publish] superducker.exe -^> publish\app
dotnet publish "%APP_PROJ%" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false %OBF_FLAG% -o "%PUB_APP%"
if errorlevel 1 goto :fail

echo [Publish] sd.exe -^> publish\cli
dotnet publish "%CLI_PROJ%" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false %OBF_FLAG% -o "%PUB_CLI%"
if errorlevel 1 goto :fail

echo [Publish] superducker-repo.exe -^> publish\repo
REM Repo is a pure ASP.NET Core service; it has no Obfuscar.xml, so we skip obfuscation.
dotnet publish "%REPO_PROJ%" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o "%PUB_REPO%"
if errorlevel 1 goto :fail

echo.
echo ============ Done ============
echo Version %CUR_VER% published%OBF_FLAG%:
echo   - %PUB_APP%\superducker.exe
echo   - %PUB_CLI%\sd.exe
echo   - %PUB_REPO%\superducker-repo.exe   (LAN shop service, listens on http://0.0.0.0:5180)
goto :eof

:fail
echo.
echo [ERROR] Build failed. Old publish files are untouched.
exit /b 1
