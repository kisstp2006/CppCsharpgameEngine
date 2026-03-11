@echo off
setlocal EnableDelayedExpansion

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

set "MONO_ROOT=%~2"
if "%MONO_ROOT%"=="" set "MONO_ROOT=C:\Program Files\Mono"

set "CMAKE_GENERATOR=%~3"
if "%CMAKE_GENERATOR%"=="" set "CMAKE_GENERATOR=AUTO"

set "CMAKE_ARCH=%~4"
if "%CMAKE_ARCH%"=="" set "CMAKE_ARCH=x64"

pushd "%~dp0" >nul

if /I "%CMAKE_GENERATOR%"=="AUTO" (
	set "CONFIGURED=0"
	for %%G in ("Visual Studio 18 2026" "Visual Studio 17 2022" "Visual Studio 16 2019" "Ninja" "NMake Makefiles") do (
		echo [1/4] Configuring CMake with generator: "%%~G" arch: "%CMAKE_ARCH%" Mono root: "%MONO_ROOT%"
		call :configure_with_generator "%%~G" "%CMAKE_ARCH%" "%MONO_ROOT%"
		if not errorlevel 1 (
			set "CMAKE_GENERATOR=%%~G"
			set "CONFIGURED=1"
			goto :after_auto_configure
		)
	)
	if "!CONFIGURED!"=="0" goto :fail
) else (
	echo [1/4] Configuring CMake with generator: "%CMAKE_GENERATOR%" arch: "%CMAKE_ARCH%" Mono root: "%MONO_ROOT%"
	call :configure_with_generator "%CMAKE_GENERATOR%" "%CMAKE_ARCH%" "%MONO_ROOT%"
	if errorlevel 1 goto :fail
)

:after_auto_configure

echo [1/4] Configure succeeded using generator: "%CMAKE_GENERATOR%"

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

:configure_with_generator
set "_GEN=%~1"
set "_ARCH=%~2"
set "_MONO=%~3"

echo %_GEN% | findstr /I "Visual Studio" >nul
if errorlevel 1 (
	cmake --fresh -S . -B build -G "%_GEN%" -DMono_ROOT="%_MONO%"
) else (
	cmake --fresh -S . -B build -G "%_GEN%" -A "%_ARCH%" -DMono_ROOT="%_MONO%"
)
exit /b %errorlevel%

:fail
echo.
echo Build failed.
popd >nul
exit /b 1
