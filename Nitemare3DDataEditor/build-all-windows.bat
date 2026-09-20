@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"

call "%ROOT%build-win-x86.bat"
if errorlevel 1 (
  echo [ERROR] win-x86 build failed.
  exit /b 1
)

call "%ROOT%build-win-x64.bat"
if errorlevel 1 (
  echo [ERROR] win-x64 build failed.
  exit /b 1
)

echo.
echo Both builds completed successfully.
echo x86: %ROOT%bin\publish\win-x86
echo x64: %ROOT%bin\publish\win-x64
endlocal

