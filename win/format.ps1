Write-Host "Formatting Make Your Choice for Windows..." -ForegroundColor Cyan
Write-Host ""

# Check if dotnet is installed
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "❌ Error: .NET SDK is not installed." -ForegroundColor Red
    Write-Host "Please install .NET SDK from https://dotnet.microsoft.com/download"
    exit 1
}

# Run dotnet format
Write-Host "🧹 Running dotnet format..." -ForegroundColor Cyan
dotnet format make-your-choice.sln

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "✅ Formatting complete!" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "❌ Formatting failed!" -ForegroundColor Red
    exit 1
}
