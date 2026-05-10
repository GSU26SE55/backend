namespace EmailService.IntegrationTests.Fixtures;

/// <summary>
/// Share 1 EmailServiceFactory cho mọi class trong collection — boot Program.cs 1 lần.
/// </summary>
[CollectionDefinition("EmailServiceIntegration")]
public class EmailServiceIntegrationCollection : ICollectionFixture<EmailServiceFactory>
{
}
