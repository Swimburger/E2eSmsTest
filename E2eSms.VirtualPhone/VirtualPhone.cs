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

/// <summary>Send and receive text messages using Twilio.</summary>
public class VirtualPhone : IAsyncDisposable
{
    private readonly WebApplication webApplication;
    private readonly PhoneNumber fromPhoneNumber;
    private readonly Dictionary<string, Conversation> conversations = new();

    /// <summary>Send and receive text messages using Twilio.</summary>
    /// <param name="webApplication">ASP.NET Core web application that listens to the Twilio SMS webhook.</param>
    /// <param name="fromPhoneNumber">The Twilio phone number from which texts will be sent.</param>
    private VirtualPhone(WebApplication webApplication, PhoneNumber fromPhoneNumber)
    {
        this.webApplication = webApplication;
        this.fromPhoneNumber = fromPhoneNumber;
    }

    /// <summary>Create a VirtualPhone using the given configuration.</summary>
    /// <param name="configuration">
    /// Configuration that should provide the following elements: 
    /// "VirtualPhone": {
    ///     // Pick an available URL, but you have to tunnel it to the internet, 
    ///     // and then configure the Twilio message webhook to point to the public URL wiht /message as path.
    ///     "Urls": "http://localhost:5000", 
    ///     "PhoneNumber": "[YOUR_TWILIO_PHONE_NUMBER]",
    ///     "Twilio": {
    ///         "Client": {}, // See https://github.com/twilio-labs/twilio-aspnet#add-the-twilio-client-to-the-aspnet-core-dependency-injection-container
    ///         "RequestValidation": {} // See https://github.com/twilio-labs/twilio-aspnet#validate-requests-in-aspnet-core
    ///     }
    /// }
    /// </param>
    /// <returns>The created VirtualPhone</returns>
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

    /// <summary>
    /// Change the service of a given type to the given lifetime.
    /// Read more about this here: https://swimburger.net/blog/dotnet/change-the-servicelifetime-after-the-service-has-been-added-to-the-net-servicecollection
    /// </summary>
    /// <param name="services">ServiceCollection to modify</param>
    /// <param name="serviceType">The type to update in the services</param>
    /// <param name="lifetime">The desired lifetime</param>
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

    /// <summary>The Minimal API endpoint that is called by Twilio when a message is received.</summary>
    /// <param name="request">Incoming HTTP request</param>
    /// <param name="twilioClient">Twilio's REST client injected by DI container</param>
    /// <returns></returns>
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

    /// <summary>Create a conversation between your VirtualPhone and the To phone number</summary>
    /// <param name="to">The phone number to send messages to and receive messages from</param>
    /// <returns>Created conversation</returns>
    public Conversation CreateConversation(PhoneNumber to)
    {
        var conversation = new Conversation(this, to);
        conversations.Add(to.ToString(), conversation);
        return conversation;
    }

    /// <summary>Remove a conversation from the VirtualPhone</summary>
    /// <param name="to">The phone number to send messages to and receive messages from</param>
    internal void RemoveConversation(PhoneNumber to) => conversations.Remove(to.ToString());

    /// <summary>Send a text message.</summary>
    /// <param name="to">The phone number to send a message to</param>
    /// <param name="body">The body of the text message</param>
    /// <returns>Created message</returns>
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

    /// <summary>
    /// Disposes the VirtualPhone and stops the web server.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await webApplication.StopAsync();
        await webApplication.DisposeAsync();
    }
}

public class Conversation : IDisposable
{
    /// <summary>The VirtualPhone to send messages from and receive messages at.</summary>
    private readonly VirtualPhone virtualPhone;
    /// <summary>Channel to write incoming messages to and read incoming messages from.</summary>
    private readonly Channel<MessageResource> incomingMessageChannel = Channel.CreateUnbounded<MessageResource>();

    /// <param name="to">The phone number to send messages to and receive messages from</param>
    public PhoneNumber To { get; init; }

    /// <summary>Create a conversation between your VirtualPhone and the To phone number.</summary>
    /// <param name="virtualPhone">The VirtualPhone to send messages from and receive messages at</param>
    /// <param name="to">The phone number to send messages to and receive messages from</param>
    /// <returns>Created conversation</returns>
    internal Conversation(VirtualPhone virtualPhone, PhoneNumber to)
    {
        this.virtualPhone = virtualPhone;
        To = to;
    }

    /// <summary>Send a text message.</summary>
    /// <param name="body">The body of the text message</param>
    /// <returns>Created message</returns>
    public async Task<MessageResource> SendMessage(string body)
    {
        var message = await virtualPhone.SendMessage(To, body);
        return message;
    }

    /// <summary>Called when a message is received from the To phone number, addressed to the VirtualPhone.From number.</summary>
    /// <param name="message">The incoming message</param>
    internal void OnMessageReceived(MessageResource message)
    {
        // no need to wait Task, discard
        _ = incomingMessageChannel.Writer.WriteAsync(message);
    }

    /// <summary>Waits for a single message to be received from the To phone number.</summary>
    /// <param name="timeToWait">The amount of time to wait before cancelling the operation</param>
    /// <returns>The received message</returns>
    public async ValueTask<MessageResource> WaitForMessage(TimeSpan timeToWait)
    {
        using var cts = new CancellationTokenSource(timeToWait);
        return await incomingMessageChannel.Reader.ReadAsync(cts.Token);
    }

    /// <summary>Waits for multiple messages to be received from the To phone number.</summary>
    /// <param name="amountOfMessages">The amount of messages to wait for.</param>
    /// <param name="timeToWait">The amount of time to wait before cancelling the operation</param>
    /// <returns>The received messages</returns>
    public async Task<MessageResource[]> WaitForMessages(int amountOfMessages, TimeSpan timeToWait)
    {
        var messages = new MessageResource[amountOfMessages];
        using var cts = new CancellationTokenSource(timeToWait);
        for (int i = 0; i < amountOfMessages; i++)
        {
            messages[i] = await incomingMessageChannel.Reader.ReadAsync(cts.Token);
        }

        return messages;
    }

    /// <summary>
    /// Dipose the conversation which removes it from the VirtualPhone.
    /// </summary>
    public void Dispose()
    {
        virtualPhone.RemoveConversation(To);
    }
}