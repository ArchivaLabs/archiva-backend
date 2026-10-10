namespace Archiva.Application.FunctionalTests.Infrastructure;

[NonParallelizable]
public abstract class TestBase
{
    [SetUp]
    public Task SetUp() => TestApp.ResetAsync();
}
