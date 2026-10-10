using Archiva.Shared;

namespace Archiva.TestAppHost;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        builder.AddSqlServer(Services.DatabaseServer).AddDatabase(Services.Database);

        var storage = builder.AddAzureStorage("storage").RunAsEmulator();
        storage.AddBlobs(Services.BlobStorage);
        storage.AddQueues(Services.AnalysisQueues);

        builder.Build().Run();
    }
}
