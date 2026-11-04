# OmniControl Quick Launcher
$dotnetPath = "C:\Program Files\dotnet"
if ($env:PATH -notlike "*$dotnetPath*") {
    $env:PATH = "$dotnetPath;$env:PATH"
}

$exePath = Join-Path $PSScriptRoot "src\OmniControl.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\OmniControl.App.exe"
if (-not (Test-Path $exePath)) {
    $exePath = Join-Path $PSScriptRoot "src\OmniControl.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\OmniControl.App.exe"
}
if (Test-Path $exePath) {
    Write-Host "Launching OmniControl..." -ForegroundColor Cyan
    Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath -Parent)
} else {
    Write-Host "Building and launching OmniControl..." -ForegroundColor Yellow
    dotnet run --project (Join-Path $PSScriptRoot "src\OmniControl.App\OmniControl.App.csproj") -c Release
}
