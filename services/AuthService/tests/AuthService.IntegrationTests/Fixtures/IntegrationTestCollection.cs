namespace AuthService.IntegrationTests.Fixtures;

/// <summary>
/// Collection definition để share 1 AuthApiFactory (1 PostgreSQL container) cho TẤT CẢ test class
/// trong "Integration" collection. Tests trong cùng collection chạy tuần tự.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<AuthApiFactory>
{
    // Marker class — không cần code, chỉ để xUnit nhận diện collection.
}

/// <summary>
/// Collection tuần tự riêng cho các test cần background outbox relay thật. Không dùng chung factory
/// với các test claim/Pending vì relay có thể thắng race trước assertion của test.
/// </summary>
[CollectionDefinition("RelayIntegration")]
public class RelayIntegrationTestCollection : ICollectionFixture<RelayEnabledAuthApiFactory>
{
    // Marker class — không cần code, chỉ để xUnit nhận diện collection.
}
