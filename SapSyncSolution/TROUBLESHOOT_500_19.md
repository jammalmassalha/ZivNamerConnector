# IIS Troubleshooting Guide - HTTP 500.19 Error

## Error: HTTP 500.19 - Invalid Configuration

**Cause**: The web.config has invalid XML or references missing IIS modules.

## Quick Fix Steps

### Step 1: Verify .NET 8.0 Hosting Bundle is Installed

```powershell
# Check if .NET is installed
dotnet --version

# If missing, download and install from:
# https://dotnet.microsoft.com/en-us/download/dotnet/8.0
# Choose "Hosting Bundle" for Windows Server
```

### Step 2: Verify AspNetCoreModuleV2 is Registered in IIS

```powershell
# Run this command
appcmd list config "Default Web Site/sapsync" -section:system.webServer/handlers

# Should show aspNetCore handler registered
# If not, reinstall the .NET 8.0 Hosting Bundle
```

### Step 3: Validate web.config XML

```powershell
# Check if web.config has valid XML
[xml](Get-Content 'C:\inetpub\wwwroot\sapsync\web.config')

# If error, use Notepad to check for syntax issues
```

### Step 4: Check IIS Logs

```powershell
# View detailed error logs
Get-Content "C:\inetpub\logs\LogFiles\W3SVC1\u_ex*.log" -Tail 50

# Look for error codes (0x8007000d = invalid configuration data)
```

## Common Solutions

### Solution 1: Reinstall .NET 8.0 Hosting Bundle

```powershell
# This fixes most 500.19 errors
1. Download: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
2. Select "Hosting Bundle" for your OS
3. Run the installer
4. Restart IIS:
   iisreset.exe /restart
```

### Solution 2: Fix web.config Syntax

Make sure your web.config looks like this (minimal):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <handlers>
      <clear />
      <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
    </handlers>
    <aspNetCore processPath="bin\Release\net8.0\SapSync.Api.exe" 
                arguments="" 
                stdoutLogEnabled="false"
                hostingModel="outofprocess" />
  </system.webServer>
</configuration>
```

### Solution 3: Reset IIS

```powershell
# Stop and restart all IIS services
iisreset.exe /stop
iisreset.exe /start

# Or restart a specific app pool
Restart-WebAppPool -Name SapSync
```

### Solution 4: Check Event Viewer

```powershell
# Open Event Viewer and check:
# Windows Logs → Application
# Look for errors from "IIS Web Core" or ".NET Runtime"

# Or via PowerShell:
Get-EventLog -LogName Application -Source "IIS Web Core" -Newest 10
```

## Verification Script

Run this script to verify your setup:

```powershell
# From command prompt in SapSyncSolution directory:
VERIFY_IIS_SETUP.bat
```

## Manual Verification

If the script doesn't work, manually verify:

```powershell
# 1. Check .NET is installed
dotnet --version

# 2. Check IIS is running
Get-Service W3SVC | Select-Object Status

# 3. Check application pool exists
Get-WebAppPool -Name SapSync

# 4. Check IIS application exists
Get-WebApplication -Name sapsync

# 5. Check physical path exists
Test-Path "C:\inetpub\wwwroot\sapsync"

# 6. Check web.config exists and is valid XML
Test-Path "C:\inetpub\wwwroot\sapsync\web.config"
[xml](Get-Content 'C:\inetpub\wwwroot\sapsync\web.config')

# 7. Check permissions
icacls "C:\inetpub\wwwroot\sapsync"
```

## Detailed Troubleshooting by Error Code

### 0x8007000d - Invalid Configuration Data

**Possible causes**:
- Missing AspNetCoreModuleV2
- Invalid XML in web.config
- Missing IIS handlers

**Solutions**:
1. Reinstall .NET 8.0 Hosting Bundle
2. Validate web.config XML
3. Clear and re-add handlers

```powershell
appcmd reset config "Default Web Site/sapsync" /section:handlers
```

### 0x80070005 - Access Denied

**Possible causes**:
- Incorrect folder permissions
- App pool identity can't read files

**Solutions**:
```powershell
# Grant read/execute to app pool
icacls "C:\inetpub\wwwroot\sapsync" /grant "IIS AppPool\SapSync:(OI)(CI)(R,RX)" /T

# Grant modify for logs directory
icacls "C:\inetpub\wwwroot\sapsync\logs" /grant "IIS AppPool\SapSync:(OI)(CI)(M)" /T
```

### 0x80004005 - Generic Failure

**Possible causes**:
- App crashed during startup
- Missing dependencies (SAP NCo)
- Database connection issues

**Solutions**:
1. Check application logs in `C:\inetpub\wwwroot\sapsync\logs\`
2. Check event viewer for .NET runtime errors
3. Check appsettings.json configuration

## Additional Resources

- [ASP.NET Core on IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/)
- [.NET 8.0 Download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- [IIS Configuration Reference](https://www.iis.net/learn)
- [Windows Event Logs](https://learn.microsoft.com/en-us/windows/win32/eventlog/event-logging)

## Support

If you still have issues:

1. Run `VERIFY_IIS_SETUP.bat` and share the output
2. Check Windows Event Viewer → Application logs
3. Check IIS logs: `C:\inetpub\logs\LogFiles\W3SVC1\u_ex*.log`
4. Check app logs: `C:\inetpub\wwwroot\sapsync\*.log`
5. Ensure .NET 8.0 Hosting Bundle is properly installed
