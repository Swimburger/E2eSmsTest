namespace E2eSms.Test;

[CollectionDefinition("TestCollection")]
public class TestCollectionFixture : 
    ICollectionFixture<ConfigurationFixture>, 
    ICollectionFixture<VirtualPhoneFixture>
{
}
