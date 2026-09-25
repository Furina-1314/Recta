@echo off
setlocal
REM ============================================================
REM  以测试数据库(Neon recta-test 分支)启动 Recta。
REM  在此实例里造的任何数据都只落在测试分支,生产分支零污染。
REM  连接串来源:环境变量 RECTA_TEST_DATABASE_URL,或本脚本同目录的 .env.test.local。
REM  重置测试数据:Neon Console 将 recta-test 分支 Reset from parent,
REM  或在 SQL Editor 执行 db/reset-test.sql。
REM ============================================================

set "DATABASE_URL="
if defined RECTA_TEST_DATABASE_URL set "DATABASE_URL=%RECTA_TEST_DATABASE_URL%"
if defined DATABASE_URL goto :launch

if not exist "%~dp0..\.env.test.local" (
    echo [错误] 未找到 .env.test.local 测试数据库连接串。
    pause
    exit /b 1
)
for /f "usebackq tokens=1,* delims==" %%A in ("%~dp0..\.env.test.local") do (
    if /i "%%A"=="DATABASE_URL" set "DATABASE_URL=%%~B"
)

:launch
if not defined DATABASE_URL (
    echo [错误] .env.test.local 中没有 DATABASE_URL。
    pause
    exit /b 1
)

set "APP=%~dp0..\desktop\Recta.App\bin\Release\net10.0\Recta.App.exe"
if exist "%APP%" goto :run
set "APP=%LOCALAPPDATA%\Programs\Recta\Recta.App.exe"
if exist "%APP%" goto :run
echo [错误] 未找到 Recta.App.exe，请先构建(desktop)或安装。
pause
exit /b 1

:run
echo 正在以测试数据库启动 Recta(此实例的数据不影响生产)...
start "" "%APP%"
