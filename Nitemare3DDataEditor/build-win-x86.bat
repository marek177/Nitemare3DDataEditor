@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "PROJECT=%ROOT%Nitemare3DDataEditor.csproj"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo [ERROR] .NET SDK 8.0 was not found in PATH.
  echo Install the .NET 8 SDK and run this script again.
  exit /b 1
)

for /f "tokens=*" %%v in ('dotnet --version') do set "SDK_VERSION=%%v"
echo Using .NET SDK %SDK_VERSION%
dotnet --list-sdks | findstr /b "8." >nul
if errorlevel 1 (
  echo [ERROR] .NET 8 SDK is required.
  exit /b 1
)

dotnet restore "%PROJECT%"
if errorlevel 1 exit /b %errorlevel%
dotnet publish "%PROJECT%" -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%ROOT%bin\publish\win-x86"
if errorlevel 1 exit /b %errorlevel%

echo.
echo Build completed: %ROOT%bin\publish\win-x86
endlocal

