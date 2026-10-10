using Archiva.Application.Common.Models;
using Archiva.Infrastructure.Analysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;

namespace Archiva.Application.UnitTests.Analysis;

public class AzureOpenAISummarizerTests
{
    [Test]
    public async Task MissingProviderConfigurationIsReportedBeforeAnyNetworkCall()
    {
        var summarizer = new AzureOpenAISummarizer(
            new ConfigurationBuilder().Build(),
            Options.Create(new DocumentAnalysisOptions())
        );

        summarizer.IsConfigured.ShouldBeFalse();
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            summarizer.SummarizeAsync("minutes.txt", "private text")
        );
        error.Message.ShouldContain("not configured");
    }
}
