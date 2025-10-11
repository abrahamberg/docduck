using FluentAssertions;
using Indexer.Providers;
using Xunit;

namespace Indexer.Tests.Unit.Providers;

[Trait("Category", "Unit")]
public class MimeTypeHelperTests
{
    [Theory]
    [InlineData(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(".doc", "application/msword")]
    [InlineData(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(".xls", "application/vnd.ms-excel")]
    [InlineData(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData(".ppt", "application/vnd.ms-powerpoint")]
    public void GetMimeType_OfficeDocuments_ReturnsCorrectMimeType(string extension, string expectedMimeType)
    {
        // Act
        var result = MimeTypeHelper.GetMimeType(extension);

        // Assert
        result.Should().Be(expectedMimeType);
    }

    [Fact]
    public void GetMimeType_PdfExtension_ReturnsApplicationPdf()
    {
        // Act
        var result = MimeTypeHelper.GetMimeType(".pdf");

        // Assert
        result.Should().Be("application/pdf");
    }

    [Theory]
    [InlineData(".txt", "text/plain")]
    [InlineData(".md", "text/markdown")]
    [InlineData(".csv", "text/csv")]
    public void GetMimeType_TextFiles_ReturnsCorrectMimeType(string extension, string expectedMimeType)
    {
        // Act
        var result = MimeTypeHelper.GetMimeType(extension);

        // Assert
        result.Should().Be(expectedMimeType);
    }

    [Theory]
    [InlineData(".json", "application/json")]
    [InlineData(".xml", "application/xml")]
    [InlineData(".yaml", "application/x-yaml")]
    [InlineData(".yml", "application/x-yaml")]
    [InlineData(".html", "text/html")]
    [InlineData(".htm", "text/html")]
    public void GetMimeType_StructuredDataFiles_ReturnsCorrectMimeType(string extension, string expectedMimeType)
    {
        // Act
        var result = MimeTypeHelper.GetMimeType(extension);

        // Assert
        result.Should().Be(expectedMimeType);
    }

    [Theory]
    [InlineData(".sql", "application/sql")]
    [InlineData(".sh", "application/x-sh")]
    [InlineData(".bat", "application/x-bat")]
    [InlineData(".ps1", "application/x-powershell")]
    [InlineData(".js", "application/javascript")]
    [InlineData(".ts", "application/typescript")]
    [InlineData(".cs", "text/x-csharp")]
    [InlineData(".py", "text/x-python")]
    public void GetMimeType_CodeFiles_ReturnsCorrectMimeType(string extension, string expectedMimeType)
    {
        // Act
        var result = MimeTypeHelper.GetMimeType(extension);

        // Assert
        result.Should().Be(expectedMimeType);
    }

    [Theory]
    [InlineData(".unknown")]
    [InlineData(".xyz")]
    [InlineData(".random")]
    public void GetMimeType_UnknownExtension_ReturnsOctetStream(string extension)
    {
        // Act
        var result = MimeTypeHelper.GetMimeType(extension);

        // Assert
        result.Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData(".PDF", "application/pdf")]
    [InlineData(".DOCX", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(".TXT", "text/plain")]
    public void GetMimeType_UppercaseExtension_IsCaseInsensitive(string extension, string expectedMimeType)
    {
        // Act
        var result = MimeTypeHelper.GetMimeType(extension);

        // Assert
        result.Should().Be(expectedMimeType);
    }

    [Theory]
    [InlineData("pdf", "application/pdf")]
    [InlineData("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("txt", "text/plain")]
    public void GetMimeType_ExtensionWithoutDot_AddsLeadingDot(string extension, string expectedMimeType)
    {
        // Act
        var result = MimeTypeHelper.GetMimeType(extension);

        // Assert
        result.Should().Be(expectedMimeType);
    }

    [Fact]
    public void GetMimeType_NullExtension_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => MimeTypeHelper.GetMimeType(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(".MkDn")]
    [InlineData(".Pdf")]
    [InlineData(".YmL")]
    public void GetMimeType_MixedCaseExtension_IsCaseInsensitive(string extension)
    {
        // Act
        var result = MimeTypeHelper.GetMimeType(extension);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }
}
