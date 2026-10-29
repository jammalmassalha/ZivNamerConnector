@echo off
REM IIS Setup Verification Script

echo =========================================
echo IIS Configuration Verification
echo =========================================
echo.

REM Check if .NET 8.0 Hosting Bundle is installed
echo [1] Checking .NET Runtime Installation...
dotnet --version
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Install .NET 8.0 Hosting Bundle
    echo Download: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
    goto error
)
echo OK: .NET installed
echo.

REM Check IIS is installed
echo [2] Checking IIS Installation...
where iisreset.exe >nul 2>&1
if errorlevel 1 (
    echo ERROR: IIS not found. Install IIS 10.0+
    goto error
)
echo OK: IIS installed
echo.

REM Check AspNetCoreModuleV2 registration
echo [3] Checking AspNetCoreModuleV2 in IIS...
appcmd list config -section:system.webServer/handlers 2>nul | findstr aspNetCore >nul
if errorlevel 1 (
    echo WARNING: AspNetCoreModuleV2 may not be registered in IIS
    echo Try reinstalling .NET 8.0 Hosting Bundle
)
echo.

REM Check Application Pool
echo [4] Checking SapSync Application Pool...
appcmd list apppools /name:SapSync >nul 2>&1
if errorlevel 1 (
    echo ERROR: Application Pool 'SapSync' not found
    echo Create it with: appcmd add apppool /name:SapSync /managedRuntimeVersion:""
    goto error
)
echo OK: Application pool exists
echo.

REM Check Application
echo [5] Checking SapSync IIS Application...
appcmd list apps /name:"Default Web Site/sapsync" >nul 2>&1
if errorlevel 1 (
    echo ERROR: IIS Application not found
    echo Create it with: appcmd add app /site.name:"Default Web Site" /path:/sapsync /physicalPath:C:\inetpub\wwwroot\sapsync
    goto error
)
echo OK: IIS Application exists
echo.

REM Check permissions
echo [6] Checking Application Directory Permissions...
if not exist "C:\inetpub\wwwroot\sapsync" (
    echo ERROR: Application directory not found
    echo Expected: C:\inetpub\wwwroot\sapsync
    goto error
)
echo OK: Application directory exists
echo.

REM Check web.config
echo [7] Checking web.config...
if not exist "C:\inetpub\wwwroot\sapsync\web.config" (
    echo ERROR: web.config not found
    echo Expected: C:\inetpub\wwwroot\sapsync\web.config
    goto error
)
echo OK: web.config exists

REM Validate XML
powershell -Command "[xml](Get-Content 'C:\inetpub\wwwroot\sapsync\web.config')" >nul 2>&1
if errorlevel 1 (
    echo ERROR: web.config has invalid XML
    goto error
)
echo OK: web.config XML is valid
echo.

REM Check appsettings.json
echo [8] Checking appsettings.json...
if not exist "C:\inetpub\wwwroot\sapsync\appsettings.json" (
    echo ERROR: appsettings.json not found
    goto error
)
echo OK: appsettings.json exists
echo.

echo =========================================
echo All checks passed! Ready for deployment.
echo =========================================
echo.
echo Next steps:
echo   1. Start the app pool: appcmd start apppool /name:SapSync
echo   2. Test the app: http://localhost/sapsync/api/sync/info
echo   3. Check logs: Get-Content C:\inetpub\wwwroot\sapsync\*.log
echo.
goto end

:error
echo.
echo =========================================
echo ERRORS FOUND - See above for details
echo =========================================
exit /b 1

:end
exit /b 0
