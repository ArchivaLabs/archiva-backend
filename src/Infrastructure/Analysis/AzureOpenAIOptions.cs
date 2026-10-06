namespace Archiva.Infrastructure.Analysis;

public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    public string? Endpoint { get; set; }
    public string? DeploymentName { get; set; }
}
