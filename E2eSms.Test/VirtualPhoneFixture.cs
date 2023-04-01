using E2eSmsTest;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace E2eSms.Test;

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