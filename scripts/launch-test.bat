@echo off
setlocal
REM ============================================================
REM  Launch Recta against the TEST database (Neon recta-test branch).
REM  Anything done in this instance stays on the test branch only.
REM
REM  Usage: scripts\launch-test.bat [full path to Recta.App.exe]
REM  Connection: env RECTA_TEST_DATABASE_URL, or repo-root .env.test.local
REM  Reset test data: Neon Console "Reset from parent" on recta-test,
REM               or run db/reset-test.sql in its SQL Editor.
REM ============================================================

set "DATABASE_URL="
set "RECTA_TEST_MODE=1"
if defined RECTA_TEST_DATABASE_URL set "DATABASE_URL=%RECTA_TEST_DATABASE_URL%"
if defined DATABASE_URL goto :findapp

if not exist "%~dp0..\.env.test.local" (
    echo [ERROR] .env.test.local not found next to the repo root.
    pause
    exit /b 1
)
for /f "usebackq tokens=1,* delims==" %%A in ("%~dp0..\.env.test.local") do (
    if /i "%%A"=="DATABASE_URL" set "DATABASE_URL=%%~B"
)

:findapp
set "APP=%~1"
if defined APP if exist "%APP%" goto :run

set "APP=F:\Recta\Recta.App.exe"
if exist "%APP%" goto :run

set "APP=%~dp0..\desktop\Recta.App\bin\Release\net10.0\Recta.App.exe"
if exist "%APP%" goto :run

set "APP=%LOCALAPPDATA%\Programs\Recta\Recta.App.exe"
if exist "%APP%" goto :run

echo [ERROR] Recta.App.exe not found.
echo   Pass it explicitly: scripts\launch-test.bat "D:\some\dir\Recta.App.exe"
pause
exit /b 1

:run
echo Launching Recta against the TEST database...
echo   app: %APP%
start "" "%APP%"
