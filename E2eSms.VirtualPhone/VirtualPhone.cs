using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Twilio.AspNet.Core;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace E2eSmsTest;

public class VirtualPhone : IAsyncDisposable
{
    private readonly WebApplication webApplication;
    private readonly PhoneNumber fromPhoneNumber;

    private TaskCompletionSource<MessageResource>? waitForResponseTaskCompletion;
    private PhoneNumber? phoneNumberToReceiveSms;

    private VirtualPhone(WebApplication webApplication, PhoneNumber fromPhoneNumber)
    {
        this.webApplication = webApplication;
        this.fromPhoneNumber = fromPhoneNumber;
    }

    public static async Task<VirtualPhone> Create(IConfiguration configuration)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.Configure<ForwardedHeadersOptions>(
            options => options.ForwardedHeaders = ForwardedHeaders.All
        );

        builder.Services
            .AddTwilioClient()
            .AddTwilioRequestValidation();
        ChangeServiceLifetime(builder.Services, typeof(ITwilioRestClient), ServiceLifetime.Singleton);
        ChangeServiceLifetime(builder.Services, typeof(TwilioRestClient), ServiceLifetime.Singleton);

        var webApplication = builder.Build();

        var virtualPhone = new VirtualPhone(webApplication, builder.Configuration["PhoneNumber"]);

        webApplication.UseForwardedHeaders();
        webApplication.MapPost("/message", virtualPhone.MessageEndpoint)
            .ValidateTwilioRequest();

        await webApplication.StartAsync();

        return virtualPhone;
    }

    private static void ChangeServiceLifetime(
        IServiceCollection services,
        Type serviceType,
        ServiceLifetime lifetime
    )
    {
        var servicesToReplace = services.Where(service => service.ServiceType == serviceType).ToList();
        foreach (var service in servicesToReplace)
        {
            services.Remove(service);
            if (service.ImplementationInstance != null)
                throw new Exception("Services with an implementation instance their lifetime cannot be changed.");
            if (service.ImplementationFactory == null)
            {
                services.Add(new ServiceDescriptor(
                    service.ServiceType,
                    service.ImplementationType!,
                    lifetime
                ));
            }
            else
            {
                services.Add(new ServiceDescriptor(
                    service.ServiceType,
                    service.ImplementationFactory,
                    lifetime
                ));
            }
        }
    }

    private async Task MessageEndpoint(
        HttpRequest request,
        [FromServices] ITwilioRestClient twilioClient
    )
    {
        var form = await request.ReadFormAsync();
        var from = form["From"];
        if (string.IsNullOrEmpty(from)) return;
        if (from.ToString() != phoneNumberToReceiveSms?.ToString()) return;

        var message = await MessageResource.FetchAsync(pathSid: form["MessageSid"], client: twilioClient);
        waitForResponseTaskCompletion?.SetResult(message);
        waitForResponseTaskCompletion = null;
    }

    public async Task SendSms(PhoneNumber to, string body)
    {
        var twilioClient = webApplication.Services.GetRequiredService<ITwilioRestClient>();
        await MessageResource.CreateAsync(
            to: to,
            from: fromPhoneNumber,
            body: body,
            client: twilioClient
        );
    }

    public async Task<MessageResource> ReceiveSms(PhoneNumber phoneNumber)
    {
        phoneNumberToReceiveSms = phoneNumber;
        waitForResponseTaskCompletion?.SetCanceled();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        cts.Token.Register(() => waitForResponseTaskCompletion?.TrySetCanceled());
        waitForResponseTaskCompletion = new TaskCompletionSource<MessageResource>();
        return await waitForResponseTaskCompletion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await webApplication.StopAsync();
        await webApplication.DisposeAsync();
    }
}