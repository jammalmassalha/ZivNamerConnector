using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SAP.Middleware.Connector;
using SapSync.Core.Models;
using SapSync.Infrastructure.Sap;

namespace SapSync.Tests;

public class SapDestinationManagerTests
{
    private static SapDestinationManager Create(SapSettings settings) =>
        new(Options.Create(settings), NullLogger<SapDestinationManager>.Instance);

    private static SapSettings Settings() => new()
    {
        DestinationName = "SAP_TEST",
        AppServerHost = "sap.example.local",
        SystemNumber = "01",
        SystemId = "QAS",
        Client = "200",
        User = "RFC",
        Password = "secret",
        Language = "DE",
        PoolSize = 3,
        PeakConnectionsLimit = 12,
        ConnectionIdleTimeout = 300,
    };

    [Fact]
    public void GetParameters_MapsLogonAndPoolingSettings()
    {
        var parameters = Create(Settings()).GetParameters("SAP_TEST");

        Assert.NotNull(parameters);
        Assert.Equal("SAP_TEST", parameters![RfcConfigParameters.Name]);
        Assert.Equal("sap.example.local", parameters[RfcConfigParameters.AppServerHost]);
        Assert.Equal("01", parameters[RfcConfigParameters.SystemNumber]);
        Assert.Equal("QAS", parameters[RfcConfigParameters.SystemID]);
        Assert.Equal("200", parameters[RfcConfigParameters.Client]);
        Assert.Equal("RFC", parameters[RfcConfigParameters.User]);
        Assert.Equal("secret", parameters[RfcConfigParameters.Password]);
        Assert.Equal("DE", parameters[RfcConfigParameters.Language]);
        Assert.Equal("3", parameters[RfcConfigParameters.PoolSize]);
        Assert.Equal("12", parameters[RfcConfigParameters.MaxPoolSize]);
        Assert.Equal("300", parameters[RfcConfigParameters.IdleTimeout]);
        Assert.False(parameters.ContainsKey(RfcConfigParameters.MessageServerHost));
    }

    [Theory]
    [InlineData("Test", "ZIT", "172.24.5.72")]
    [InlineData("Production", "ZIP", "172.24.5.25")]
    public void EnvironmentSettings_ProduceTheExpectedDestination(string environment, string destination, string host)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "api", "appsettings.json"))
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "api", $"appsettings.{environment}.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SapSettings:Password"] = "from-env" })
            .Build();
        var settings = configuration.GetSection(SapSettings.SectionName).Get<SapSettings>()!;

        var parameters = Create(settings).GetParameters(destination)!;

        Assert.Equal(destination, parameters[RfcConfigParameters.Name]);
        Assert.Equal(host, parameters[RfcConfigParameters.AppServerHost]);
        Assert.Equal("00", parameters[RfcConfigParameters.SystemNumber]);
        Assert.Equal("940", parameters[RfcConfigParameters.Client]);
        Assert.Equal("bapiapp", parameters[RfcConfigParameters.User]);
        Assert.Equal("from-env", parameters[RfcConfigParameters.Password]);
        Assert.Equal("HE", parameters[RfcConfigParameters.Language]);
        Assert.False(parameters.ContainsKey(RfcConfigParameters.SystemID)); // base "PRD" must not leak into Test/Prod
    }

    [Fact]
    public void GetParameters_TrimsUserAndHost()
    {
        var settings = Settings();
        settings.User = " bapiapp ";
        settings.AppServerHost = " 172.24.5.25";

        var parameters = Create(settings).GetParameters("SAP_TEST")!;

        Assert.Equal("bapiapp", parameters[RfcConfigParameters.User]);
        Assert.Equal("172.24.5.25", parameters[RfcConfigParameters.AppServerHost]);
    }

    [Fact]
    public void GetParameters_UsesMessageServer_WhenNoAppServerConfigured()
    {
        var settings = Settings();
        settings.AppServerHost = null;
        settings.MessageServerHost = "ms.example.local";
        settings.LogonGroup = "PUBLIC";

        var parameters = Create(settings).GetParameters("SAP_TEST")!;

        Assert.Equal("ms.example.local", parameters[RfcConfigParameters.MessageServerHost]);
        Assert.Equal("PUBLIC", parameters[RfcConfigParameters.LogonGroup]);
        Assert.False(parameters.ContainsKey(RfcConfigParameters.AppServerHost));
    }

    [Fact]
    public void GetParameters_ReturnsNull_ForUnknownDestination()
    {
        Assert.Null(Create(Settings()).GetParameters("OTHER"));
        Assert.False(Create(Settings()).ChangeEventsSupported());
    }

    [Fact]
    public void EnsureRegistered_IsThreadSafeAndRegistersOnlyOnce()
    {
        // NCo throws if a second configuration is registered, so any double registration fails this test.
        var managers = Enumerable.Range(0, 4).Select(_ => Create(Settings())).ToArray();

        Parallel.For(0, 256, i => managers[i % managers.Length].EnsureRegistered());

        managers[0].EnsureRegistered();
    }

    [Fact]
    public void SapSettings_Validation_RequiresAHostAndPoolSizeWithinPeakLimit()
    {
        var settings = Settings();
        settings.AppServerHost = null;
        settings.PoolSize = 20;

        var errors = settings.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(settings)).ToList();

        Assert.Equal(2, errors.Count);
        Assert.Empty(Settings().Validate(new System.ComponentModel.DataAnnotations.ValidationContext(settings)));
    }
}
