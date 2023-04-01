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
        await virtualPhone.SendSms(toPhoneNumber, "Hi");
        var sms = await virtualPhone.ReceiveSms(toPhoneNumber);
        Assert.Equal("Welcome to Twilio SMS.  For more information, see http://twilio.com/sms", sms.Body);
    }
}
