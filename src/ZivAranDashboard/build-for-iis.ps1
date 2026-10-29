#!/usr/bin/env powershell
# Build script for Angular ARAN Dashboard deployment to IIS sub-path
# Usage: .\build-for-iis.ps1 -BaseHref "/pbi_srv/zivarantest/"

param(
    [Parameter(Mandatory=$false)]
    [string]$BaseHref = "/pbi_srv/zivarantest/"
)

$ErrorActionPreference = "Stop"

# Normalize base href - ensure trailing slash
if (-not $BaseHref.EndsWith("/")) {
    $BaseHref = "$BaseHref/"
}

# Configuration
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputDir = Join-Path $ProjectDir "dist\ziv-aran-dashboard"
$NodeModulesDir = Join-Path $ProjectDir "node_modules"

# Display header
Write-Host "================================" -ForegroundColor Green
Write-Host "Angular ARAN Dashboard Builder" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green
Write-Host ""

Write-Host "Project Directory: $ProjectDir"
Write-Host "Base Href: $BaseHref"
Write-Host "Output Directory: $OutputDir"
Write-Host ""

# Check if node_modules exists
if (-not (Test-Path $NodeModulesDir)) {
    Write-Host "Installing dependencies..." -ForegroundColor Yellow
    npm install
    Write-Host ""
}

# Clean previous builds
Write-Host "Cleaning previous build..." -ForegroundColor Yellow
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
Write-Host "✓ Clean complete" -ForegroundColor Green
Write-Host ""

# Build production bundle
Write-Host "Building production bundle..." -ForegroundColor Yellow
$BuildProcess = Start-Process -FilePath "npm" -ArgumentList "run", "build", "--", "--configuration=production", "--base-href=`"$BaseHref`"" -NoNewWindow -PassThru -Wait

if ($BuildProcess.ExitCode -eq 0) {
    Write-Host "✓ Build successful" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "✗ Build failed" -ForegroundColor Red
    exit 1
}

# Display build results
Write-Host "Build Statistics:" -ForegroundColor Yellow
Write-Host "================================"

if (Test-Path $OutputDir) {
    $Items = Get-ChildItem -Path $OutputDir -Recurse -File
    $TotalSize = ($Items | Measure-Object -Property Length -Sum).Sum / 1MB
    $FileCount = $Items.Count
    
    Write-Host "Total Size: $([math]::Round($TotalSize, 2)) MB"
    Write-Host "Files: $FileCount"
    Write-Host ""
    
    Write-Host "Key Files:" -ForegroundColor Yellow
    
    # Check index.html
    $IndexFile = Join-Path $OutputDir "index.html"
    if (Test-Path $IndexFile) {
        $IndexSize = (Get-Item $IndexFile).Length / 1KB
        Write-Host "  index.html: $([math]::Round($IndexSize, 2)) KB"
        
        # Check for base href
        $IndexContent = Get-Content $IndexFile -Raw
        if ($IndexContent -match "base href=`"$([regex]::Escape($BaseHref))`"") {
            Write-Host "  ✓ Base href correctly set to: $BaseHref" -ForegroundColor Green
        } else {
            Write-Host "  ✗ Base href NOT found in index.html" -ForegroundColor Red
        }
    }
    
    # Check styles
    $StylesFiles = Get-ChildItem -Path $OutputDir -Filter "styles*.css" -ErrorAction SilentlyContinue
    if ($StylesFiles) {
        $StylesSize = ($StylesFiles | Measure-Object -Property Length -Sum).Sum / 1KB
        Write-Host "  Styles: $([math]::Round($StylesSize, 2)) KB"
    }
    
    # Check main bundle
    $MainFiles = Get-ChildItem -Path $OutputDir -Filter "main*.js" -ErrorAction SilentlyContinue
    if ($MainFiles) {
        $MainSize = ($MainFiles | Measure-Object -Property Length -Sum).Sum / 1KB
        Write-Host "  Main Bundle: $([math]::Round($MainSize, 2)) KB"
    }
    
    Write-Host ""
    Write-Host "Build artifacts location:" -ForegroundColor Green
    Write-Host "  $OutputDir"
    Write-Host ""
    
    Write-Host "Next Steps for IIS Deployment:" -ForegroundColor Yellow
    Write-Host "  1. Copy all files from above directory to IIS physical path"
    Write-Host "  2. Configure web.config with URL rewrite rules"
    Write-Host "  3. Update IIS Application base path to: $BaseHref"
    Write-Host "  4. Test in browser: http://your-server$BaseHref"
    Write-Host ""
} else {
    Write-Host "✗ Output directory not found" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Build complete!" -ForegroundColor Green
