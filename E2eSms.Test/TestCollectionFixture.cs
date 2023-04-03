namespace E2eSms.Test;

[CollectionDefinition("TestCollection", DisableParallelization = true)]
public class TestCollectionFixture : 
    ICollectionFixture<ConfigurationFixture>, 
    ICollectionFixture<VirtualPhoneFixture>
{
}