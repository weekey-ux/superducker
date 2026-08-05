@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

REM === SuperDucker 一键运行 ===
REM 首次运行会自动构建（调用 builder.bat），之后直接启动已发布的 superducker.exe。
REM 如需强制重新构建，删除 publish\app\superducker.exe 后重跑本脚本即可。

set "ROOT=%~dp0"
set "APP_EXE=%ROOT%publish\app\superducker.exe"

REM ---- 检查运行时是否已发布 ----
if exist "%APP_EXE%" goto :run

echo [Info] 未检测到已构建的程序，正在首次构建（可能需要几分钟）...
echo.

REM ---- 调用 builder.bat 完成构建（或用 dotnet publish 兜底） ----
if exist "%ROOT%builder.bat" (
    call "%ROOT%builder.bat"
) else (
    dotnet publish "%ROOT%SuperDucker.App\SuperDucker.App.csproj" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o "%ROOT%publish\app"
)

if not exist "%APP_EXE%" (
    echo.
    echo [ERROR] 构建失败，无法启动。请检查 .NET 8 SDK 是否已安装（dotnet --version）。
    pause
    exit /b 1
)

:run
echo [Info] 启动 SuperDucker ...
start "" "%APP_EXE%"
goto :eof
