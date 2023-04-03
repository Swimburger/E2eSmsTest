using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;
using Twilio.AspNet.Core;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace E2eSmsTest;

public class VirtualPhone : IAsyncDisposable
{
    private readonly WebApplication webApplication;
    private readonly PhoneNumber fromPhoneNumber;
    private readonly Dictionary<string, Conversation> conversations = new();

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
        var from = form["From"].ToString();
        if (string.IsNullOrEmpty(from)) return;

        if (conversations.TryGetValue(from, out var conversation))
        {
            var message = await MessageResource.FetchAsync(pathSid: form["MessageSid"], client: twilioClient);
            conversation.OnMessageReceived(message);
        }
    }

    public Conversation CreateConversation(PhoneNumber to)
    {
        var conversation = new Conversation(this, to);
        conversations.Add(to.ToString(), conversation);
        return conversation;
    }

    internal void RemoveConversation(PhoneNumber to) => conversations.Remove(to.ToString());

    internal async Task<MessageResource> SendMessage(PhoneNumber to, string body)
    {
        var twilioClient = webApplication.Services.GetRequiredService<ITwilioRestClient>();
        return await MessageResource.CreateAsync(
            to: to,
            from: fromPhoneNumber,
            body: body,
            client: twilioClient
        );
    }

    public async ValueTask DisposeAsync()
    {
        await webApplication.StopAsync();
        await webApplication.DisposeAsync();
    }
}

public class Conversation : IDisposable
{
    private readonly VirtualPhone virtualPhone;
    private readonly Channel<MessageResource> incomingMessageChannel;

    public PhoneNumber To { get; init; }

    internal Conversation(VirtualPhone virtualPhone, PhoneNumber to)
    {
        this.virtualPhone = virtualPhone;
        To = to;
        incomingMessageChannel = Channel.CreateUnbounded<MessageResource>();
    }

    public async Task<MessageResource> SendMessage(string body)
    {
        var message = await virtualPhone.SendMessage(To, body);
        return message;
    }

    internal void OnMessageReceived(MessageResource message)
    {
        _ = incomingMessageChannel.Writer.WriteAsync(message);
    }

    public async ValueTask<MessageResource> WaitForMessage(TimeSpan timeToWait)
    {
        using var cts = new CancellationTokenSource(timeToWait);
        return await incomingMessageChannel.Reader.ReadAsync(cts.Token);
    }

    public async Task<IReadOnlyList<MessageResource>> WaitForMessages(int amountOfMessages, TimeSpan timeToWait)
    {
        var messages = new List<MessageResource>(amountOfMessages);
        using var cts = new CancellationTokenSource(timeToWait);
        for (; amountOfMessages > 0; amountOfMessages--)
        {
            messages.Add(await incomingMessageChannel.Reader.ReadAsync(cts.Token));
        }

        return messages;
    }

    public void Dispose()
    {
        virtualPhone.RemoveConversation(To);
    }
}