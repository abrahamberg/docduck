namespace Indexer.Tests.Integration;

/// <summary>
/// Test collection definition for S3 integration tests.
/// Ensures tests run sequentially to avoid conflicts with shared resources.
/// </summary>
[CollectionDefinition("S3Integration")]
public class S3IntegrationCollection : ICollectionFixture<S3IntegrationFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

/// <summary>
/// Test collection definition for OneDrive integration tests.
/// Ensures tests run sequentially to avoid conflicts with shared resources.
/// </summary>
[CollectionDefinition("OneDriveIntegration")]
public class OneDriveIntegrationCollection : ICollectionFixture<OneDriveIntegrationFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

/// <summary>
/// Fixture for S3 integration tests.
/// Could be used for shared setup/cleanup if needed.
/// </summary>
public class S3IntegrationFixture : IDisposable
{
    public void Dispose()
    {
        // Any shared cleanup logic
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Fixture for OneDrive integration tests.
/// Could be used for shared setup/cleanup if needed.
/// </summary>
public class OneDriveIntegrationFixture : IDisposable
{
    public void Dispose()
    {
        // Any shared cleanup logic
        GC.SuppressFinalize(this);
    }
}