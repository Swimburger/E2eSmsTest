using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace E2eSms.Test;

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