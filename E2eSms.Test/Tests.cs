using E2eSms.Test;
using Microsoft.Extensions.Configuration;
using Twilio.Types;

namespace E2eSmsTest;

[Collection("TestCollection")]
public class Tests
{
    private readonly VirtualPhone virtualPhone;
    private readonly IConfigurationRoot configuration;
    private readonly PhoneNumber toPhoneNumber;

    public Tests(VirtualPhoneFixture virtualPhoneFixture, ConfigurationFixture configurationFixture)
    {
        virtualPhone = virtualPhoneFixture.VirtualPhone!;
        configuration = configurationFixture.Configuration;
        toPhoneNumber = new PhoneNumber(configuration["ToPhoneNumber"]);
    }

    [Fact]
    public async Task Response_Should_Be_Welcome_Message()
    {
        using var conversation = virtualPhone.CreateConversation(toPhoneNumber);
        await conversation.SendMessage("Hi");
        var message = await conversation.WaitForMessage(TimeSpan.FromSeconds(10));
        Assert.Equal("Welcome to Twilio SMS.  For more information, see http://twilio.com/sms", message.Body);
    }

    [Fact]
    public async Task Response_Should_Be_Welcome_Message_Twice()
    {
        using var conversation = virtualPhone.CreateConversation(toPhoneNumber);
        await conversation.SendMessage("Hi");
        await conversation.SendMessage("Hi again");

        await Task.Delay(TimeSpan.FromSeconds(4));

        Assert.Equal(4, conversation.Messages.Count);

        Assert.Equal("Welcome to Twilio SMS.  For more information, see http://twilio.com/sms", conversation.Messages[2].Body);
        Assert.Equal("Welcome to Twilio SMS.  For more information, see http://twilio.com/sms", conversation.Messages[3].Body);
    }

    [Fact]
    public async Task Verify_Survey_Pineapple_And_Cake()
    {
        using var conversation = virtualPhone.CreateConversation(toPhoneNumber);
        // start conversation
        await conversation.SendMessage("Hi");

        // pineapple on pizza question
        var message = await conversation.WaitForMessage(TimeSpan.FromSeconds(10));
        Assert.Equal("On a scale of 1-10, how much do you like pineapple on pizza?", message.Body);
        await conversation.SendMessage("10");

        // cake vs pie question
        message = await conversation.WaitForMessage(TimeSpan.FromSeconds(10));
        Assert.Equal("Cake or pie?", message.Body);
        await conversation.SendMessage("Cake");
        message = await conversation.WaitForMessage(TimeSpan.FromSeconds(10));
        Assert.Equal("The cake is a lie.", message.Body);
    }
}
