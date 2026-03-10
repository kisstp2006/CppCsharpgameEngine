@echo off
setlocal

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

set "MONO_ROOT=%~2"
if "%MONO_ROOT%"=="" set "MONO_ROOT=C:\Program Files\Mono"

pushd "%~dp0" >nul

echo [1/4] Configuring CMake with Mono root: "%MONO_ROOT%"
cmake -S . -B build -DMono_ROOT="%MONO_ROOT%"
if errorlevel 1 goto :fail

echo [2/4] Building native targets (App + Editor) [%CONFIG%]
cmake --build build --config %CONFIG%
if errorlevel 1 goto :fail

echo [3/4] Building gameplay scripts [%CONFIG%]
dotnet build scripts\GameScripts.csproj -c %CONFIG%
if errorlevel 1 goto :fail

echo [4/4] Building editor scripts [%CONFIG%]
dotnet build editor\EngineEditor.csproj -c %CONFIG%
if errorlevel 1 goto :fail

echo.
echo Build completed successfully.
echo Run editor: .\build\%CONFIG%\Editor.exe
echo Run app:    .\build\%CONFIG%\App.exe

popd >nul
exit /b 0

:fail
echo.
echo Build failed.
popd >nul
exit /b 1
