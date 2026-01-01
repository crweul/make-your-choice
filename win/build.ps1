Write-Host "Building Make Your Choice for Windows…" -ForegroundColor Cyan
Write-Host ""

# Check if dotnet is installed
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "❌ Error: .NET SDK is not installed." -ForegroundColor Red
    Write-Host "Please install .NET 10.0 SDK or later from https://dotnet.microsoft.com/download"
    exit 1
}

# Check .NET version
$dotnetVersion = dotnet --version
Write-Host "📦 Using .NET SDK version: $dotnetVersion" -ForegroundColor Green
Write-Host ""

# Clean previous builds
if (Test-Path "bin") {
    Write-Host "Cleaning previous builds…" -ForegroundColor Yellow
    Remove-Item -Recurse -Force bin, obj -ErrorAction SilentlyContinue
}

# Build in release mode with single-file publish
Write-Host "📦 Building in release mode…" -ForegroundColor Cyan
dotnet publish "MakeYourChoice.csproj" -c Release -r win-x64 --self-contained true

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "✅ Build successful!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Binary location:" -ForegroundColor Cyan
    Write-Host "   .\bin\Release\net10.0-windows\win-x64\publish\MakeYourChoice.exe"
    Write-Host ""
    Write-Host "Note: The application requires Administrator privileges to modify the hosts file" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}
