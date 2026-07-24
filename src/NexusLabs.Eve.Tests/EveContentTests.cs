using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveContentTests
{
    [Test]
    public async Task CreateFile_ProducesAiSdkCompatibleDataUrl()
    {
        EveContentPart part = EveContentPart.CreateFile(
            Encoding.UTF8.GetBytes("Hi"),
            "text/plain",
            "greeting.txt");

        await Assert.That(part.Json.GetProperty("type").GetString()).IsEqualTo("file");
        await Assert.That(part.Json.GetProperty("mediaType").GetString()).IsEqualTo("text/plain");
        await Assert.That(part.Json.GetProperty("filename").GetString()).IsEqualTo("greeting.txt");
        await Assert.That(part.Json.GetProperty("data").GetString())
            .IsEqualTo("data:text/plain;base64,SGk=");
    }

    [Test]
    public async Task FromParts_PreservesPartOrder()
    {
        EveMessageContent message = EveMessageContent.FromParts(
            EveContentPart.CreateText("Summarize this."),
            EveContentPart.CreateFile("https://example.test/report.pdf", "application/pdf"));

        await Assert.That(message.Json.GetArrayLength()).IsEqualTo(2);
        await Assert.That(message.Json[0].GetProperty("type").GetString()).IsEqualTo("text");
        await Assert.That(message.Json[1].GetProperty("type").GetString()).IsEqualTo("file");
    }
}
