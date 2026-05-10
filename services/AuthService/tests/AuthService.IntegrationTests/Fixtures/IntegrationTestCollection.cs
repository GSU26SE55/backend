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
