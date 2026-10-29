# IIS Deployment Guide for SapSync API

## Prerequisites

1. **Windows Server 2016+** with IIS 10.0+
2. **.NET 8.0 Hosting Bundle** installed
   - Download: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
   - Choose "Hosting Bundle" for Windows Server
3. **URL Rewrite Module 2.1** (optional, for SPA routing)
4. **Application Request Routing (ARR)** (optional, for load balancing)

## Installation Steps

### Step 1: Install .NET 8.0 Hosting Bundle

```powershell
# Download and run the installer
# https://dotnet.microsoft.com/download/dotnet/8.0
# Select "Hosting Bundle" for your OS

# Verify installation
dotnet --version
```

### Step 2: Publish the Application

```powershell
cd SapSyncSolution
dotnet publish -c Release -o "C:\inetpub\wwwroot\sapsync"
```

### Step 3: Create IIS Application Pool

```powershell
# Open IIS Manager
# Right-click Application Pools → Add Application Pool
# - Name: SapSync
# - .NET CLR Version: No Managed Code
# - Managed pipeline mode: Integrated
# - Identity: ApplicationPoolIdentity (or specific account if needed)
```

### Step 4: Create IIS Application

```powershell
# In IIS Manager:
# Right-click Default Website → Add Application
# - Alias: sapsync
# - Physical path: C:\inetpub\wwwroot\sapsync
# - Application pool: SapSync
```

### Step 5: Configure Permissions

```powershell
# Grant IIS app pool identity read/execute permissions
icacls "C:\inetpub\wwwroot\sapsync" /grant "IIS AppPool\SapSync:(OI)(CI)(R,RX)" /T

# Grant write permissions for logs
icacls "C:\inetpub\wwwroot\sapsync\logs" /grant "IIS AppPool\SapSync:(OI)(CI)(M)" /T
```

### Step 6: Configure appsettings.json

Update `C:\inetpub\wwwroot\sapsync\appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "SqlServer": "Data Source=YOUR_SERVER;Initial Catalog=SapSync;User ID=YOUR_USER;Password=YOUR_PASS;TrustServerCertificate=True;"
  },
  "SapSettings": {
    "DestinationName": "PORIA",
    "AppServerHost": "172.24.2.25",
    "SystemNumber": "01",
    "SystemId": "PRD",
    "Client": "933",
    "User": "RFCUSER",
    "Password": "Rf123456*",
    "Language": "HE",
    "PoolSize": 5,
    "PeakConnectionsLimit": 10,
    "ConnectionIdleTimeout": 600,
    "ConvertAbapTypes": true
  },
  "SqlSettings": {
    "ConnectionStringName": "SqlServer",
    "BulkCopyBatchSize": 5000,
    "BulkCopyTimeoutSeconds": 600,
    "CommandTimeoutSeconds": 600
  }
}
```

### Step 7: Create Logs Directory

```powershell
mkdir "C:\inetpub\wwwroot\sapsync\logs"
icacls "C:\inetpub\wwwroot\sapsync\logs" /grant "IIS AppPool\SapSync:(OI)(CI)(M)" /T
```

### Step 8: Start Application Pool

```powershell
# In IIS Manager:
# - Select Application Pool: SapSync
# - Right-click → Start

# Or via PowerShell:
Start-WebAppPool -Name SapSync
```

### Step 9: Verify Deployment

```powershell
# Check if app is running
Get-WebAppPool -Name SapSync

# Test the API
Invoke-WebRequest -Uri "http://localhost/sapsync/api/sync/info" -UseBasicParsing

# Check logs
Get-Content "C:\inetpub\wwwroot\sapsync\logs\stdout_*.log"
```

## Troubleshooting

### 500 Error - Check Logs

```powershell
# Stdout logs
Get-Content "C:\inetpub\wwwroot\sapsync\logs\stdout_*.log" -Tail 50

# IIS logs
Get-Content "C:\inetpub\logs\LogFiles\W3SVC1\u_ex*.log" -Tail 50
```

### Application Pool Crashes

**Cause**: SAP NCo library issues or missing dependencies

**Solution**:
1. Check if SAP NCo x64 bits are installed
2. Verify all dependencies are present
3. Check event viewer for detailed errors

### Connection String Issues

**Cause**: Database connectivity problems

**Solution**:
1. Test connection from the server
2. Verify SQL Server is accessible
3. Check firewall rules

### Port Already in Use

**Cause**: Another app using the same port

**Solution**: Use different port in web.config or change IIS binding

## Performance Tuning

### IIS Application Pool Settings

```powershell
# In IIS Manager → Application Pools → SapSync → Advanced Settings:
- Queue Length: 1000
- Process Model → Max Processes: 1
- Process Model → Recycle → Specific Times: (set daily restart)
- CPU Settings → Action: Throttle (at 95%)
```

### web.config Optimization

```xml
<!-- In web.config httpCompression section -->
<httpCompression directory="%SystemDrive%\inetpub\temp\IIS Temporary Compressed Files">
  <scheme name="gzip" dll="%Windir%\system32\inetsrv\gzip.dll" staticCompressionLevel="9" />
</httpCompression>
```

## Monitoring

### Application Insights (Optional)

Add to `appsettings.json`:

```json
{
  "ApplicationInsights": {
    "InstrumentationKey": "YOUR_KEY"
  }
}
```

### Health Check Endpoint

```powershell
# Monitor the app
$healthUrl = "http://YOUR_SERVER/sapsync/api/sync/info"
$response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing
$response.StatusCode  # Should be 200
```

## Maintenance

### Restart Application Pool

```powershell
Restart-WebAppPool -Name SapSync
```

### View Running Processes

```powershell
Get-Process | Where-Object {$_.ProcessName -like "*SapSync*"}
```

### Stop Application Pool

```powershell
Stop-WebAppPool -Name SapSync
```

## Security Best Practices

1. **Use HTTPS**: Configure SSL certificate in IIS bindings
2. **Restrict Access**: Use IP restrictions for sensitive endpoints
3. **Hide Version**: Remove `Server` header in web.config
4. **Authentication**: Enable Windows or Forms authentication if needed
5. **CORS**: Configure CORS in appsettings.json for Angular frontend

## Additional Resources

- .NET 8.0 Hosting Bundle: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
- IIS Hosting: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/
- SAP NCo Setup: Consult SAP Support Portal
