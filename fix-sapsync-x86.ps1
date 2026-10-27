# Fix SapSync.Core FileNotFoundException for x86 build
# Run this script in your project root

Write-Host "=== SapSync x86 Build Fix ===" -ForegroundColor Green
Write-Host ""

# Navigate to solution folder
$solutionPath = "C:\Users\jmassalha\Source\Repos\ZivNamerConnector\SapSyncSolution"
if (-not (Test-Path $solutionPath)) {
    Write-Host "ERROR: Solution path not found: $solutionPath" -ForegroundColor Red
    exit 1
}

Set-Location $solutionPath
Write-Host "Working directory: $(Get-Location)" -ForegroundColor Cyan

# Step 1: Remove stale build artifacts
Write-Host "`n[1/5] Removing stale build artifacts..." -ForegroundColor Yellow
@(
    ".\src\SapSync.Api\bin",
    ".\src\SapSync.Api\obj",
    ".\src\SapSync.Core\bin",
    ".\src\SapSync.Core\obj",
    ".\src\SapSync.Infrastructure\bin",
    ".\src\SapSync.Infrastructure\obj",
    ".\tests\SapSync.Tests\bin",
    ".\tests\SapSync.Tests\obj"
) | ForEach-Object {
    if (Test-Path $_) {
        Write-Host "  Removing $_"
        Remove-Item -Recurse -Force $_
    }
}

# Step 2: Restore packages
Write-Host "`n[2/5] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet restore failed" -ForegroundColor Red
    exit 1
}

# Step 3: Build with x86
Write-Host "`n[3/5] Building solution for x86..." -ForegroundColor Yellow
dotnet build -c Debug --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet build failed" -ForegroundColor Red
    exit 1
}

# Step 4: Verify binaries
Write-Host "`n[4/5] Verifying build output..." -ForegroundColor Yellow
$requiredDlls = @(
    ".\src\SapSync.Api\bin\Debug\net8.0\SapSync.Api.dll",
    ".\src\SapSync.Core\bin\Debug\net8.0\SapSync.Core.dll",
    ".\src\SapSync.Infrastructure\bin\Debug\net8.0\SapSync.Infrastructure.dll"
)

$allFound = $true
$requiredDlls | ForEach-Object {
    if (Test-Path $_) {
        Write-Host "  ✓ Found $_"
    } else {
        Write-Host "  ✗ MISSING $_" -ForegroundColor Red
        $allFound = $false
    }
}

if (-not $allFound) {
    Write-Host "`nERROR: Some DLLs are missing. Build may have failed." -ForegroundColor Red
    exit 1
}

# Step 5: Show architecture info
Write-Host "`n[5/5] Architecture verification..." -ForegroundColor Yellow
dotnet --info | Select-String "OS|Architecture|RID" | Write-Host

Write-Host "`n=== BUILD SUCCESSFUL ===" -ForegroundColor Green
Write-Host "Next steps:"
Write-Host "  1. In Visual Studio: Press F5 to run"
Write-Host "  2. Or from command line: cd src\SapSync.Api && dotnet run"
Write-Host ""
