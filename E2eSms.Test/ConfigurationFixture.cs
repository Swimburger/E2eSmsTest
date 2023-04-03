using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace E2eSms.Test;

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