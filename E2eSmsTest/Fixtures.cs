using System.Reflection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace E2eSmsTest;

/// <summary>
/// Fixture that loads configuration from appsettings.json, user secrets, and environment variables.
/// </summary>
public class ConfigurationFixture
{
    public IConfigurationRoot Configuration { get; set; }

    public ConfigurationFixture()
    {
        Configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
    }
}

/// <summary>
/// Fixture that creates the VirtualPhone using configuration section "VirtualPhone" from appsettings.json, user secrets, and environment variables.
/// </summary>
public class VirtualPhoneFixture : IAsyncLifetime
{
    public VirtualPhone? VirtualPhone { get; set; }

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "VIRTUALPHONE:")
            .Build();

        VirtualPhone = await VirtualPhone.Create(configuration.GetSection("VirtualPhone"));
    }

    public async Task DisposeAsync()
    {
        if (VirtualPhone != null)
            await VirtualPhone.DisposeAsync();
    }
}

[CollectionDefinition("TestCollection", DisableParallelization = true)]
public class TestCollectionFixture :
    ICollectionFixture<ConfigurationFixture>,
    ICollectionFixture<VirtualPhoneFixture>
{
}