@echo off
set "PATH=C:\Program Files\dotnet;%PATH%"
set "EXE_PATH=%~dp0src\OmniControl.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\OmniControl.App.exe"
if not exist "%EXE_PATH%" set "EXE_PATH=%~dp0src\OmniControl.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\OmniControl.App.exe"

if exist "%EXE_PATH%" (
    echo Launching OmniControl...
    for %%F in ("%EXE_PATH%") do (
        start "" /D "%%~dpF" "%EXE_PATH%"
    )
) else (
    echo Building and launching OmniControl...
    dotnet run --project "%~dp0src\OmniControl.App\OmniControl.App.csproj" -c Release
)
