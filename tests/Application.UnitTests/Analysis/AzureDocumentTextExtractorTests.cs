using System.Text;
using Archiva.Infrastructure.Analysis;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Shouldly;

namespace Archiva.Application.UnitTests.Analysis;

public class AzureDocumentTextExtractorTests
{
    [Test]
    public async Task ExtractsUtf8TextWithoutDocumentIntelligenceConfiguration()
    {
        const string expected = "Meeting decisions: café and next steps.";
        var extractor = new AzureDocumentTextExtractor(new ConfigurationBuilder().Build());
        var content = Encoding.UTF8.GetBytes(expected);

        var estimate = extractor.Estimate(content, "txt");
        var extracted = await extractor.ExtractAsync(content, "TXT");

        estimate.BillableUnits.ShouldBe(0);
        estimate.TextCharacters.ShouldBe(expected.Length);
        extracted.ShouldBe(expected);
    }

    [Test]
    public void EstimateRejectsMalformedUtf8Text()
    {
        var extractor = new AzureDocumentTextExtractor(new ConfigurationBuilder().Build());
        var content = new byte[] { 0xC3, 0x28 };

        Should.Throw<InvalidDataException>(() => extractor.Estimate(content, "TXT"));
    }
}
