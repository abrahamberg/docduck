using Api.Models;
using Api.Services;
using Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Api.Tests.Unit;

public class KeywordSearchServiceTests
{
    [Fact]
    public void ExtractKeywords_RemovesCommonWords()
    {
        // Arrange
        var dbOptions = Microsoft.Extensions.Options.Options.Create(new DbOptions { ConnectionString = "fake" });
        var searchOptions = Microsoft.Extensions.Options.Options.Create(new SearchOptions());
        var logger = Mock.Of<ILogger<KeywordSearchService>>();
        var service = new KeywordSearchService(dbOptions, searchOptions, logger);

        // Act
        var keywords = service.ExtractKeywords("What is the best way to configure the API?", maxKeywords: 3);

        // Assert
        Assert.NotEmpty(keywords);
        Assert.DoesNotContain("what", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("is", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("the", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(keywords, k => k.Equals("configure", StringComparison.OrdinalIgnoreCase) ||
                                       k.Equals("API", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractKeywords_LimitsToMaxKeywords()
    {
        // Arrange
        var dbOptions = Microsoft.Extensions.Options.Options.Create(new DbOptions { ConnectionString = "fake" });
        var searchOptions = Microsoft.Extensions.Options.Options.Create(new SearchOptions());
        var logger = Mock.Of<ILogger<KeywordSearchService>>();
        var service = new KeywordSearchService(dbOptions, searchOptions, logger);

        // Act
        var keywords = service.ExtractKeywords("authentication authorization database migration configuration deployment testing", maxKeywords: 3);

        // Assert
        Assert.True(keywords.Count <= 3);
    }

    [Fact]
    public void ExtractKeywords_HandlesEmptyInput()
    {
        // Arrange
        var dbOptions = Microsoft.Extensions.Options.Options.Create(new DbOptions { ConnectionString = "fake" });
        var searchOptions = Microsoft.Extensions.Options.Options.Create(new SearchOptions());
        var logger = Mock.Of<ILogger<KeywordSearchService>>();
        var service = new KeywordSearchService(dbOptions, searchOptions, logger);

        // Act
        var keywords = service.ExtractKeywords("");

        // Assert
        Assert.Empty(keywords);
    }

    [Fact]
    public void ExtractKeywords_FiltersShortWords()
    {
        // Arrange
        var dbOptions = Microsoft.Extensions.Options.Options.Create(new DbOptions { ConnectionString = "fake" });
        var searchOptions = Microsoft.Extensions.Options.Options.Create(new SearchOptions());
        var logger = Mock.Of<ILogger<KeywordSearchService>>();
        var service = new KeywordSearchService(dbOptions, searchOptions, logger);

        // Act
        var keywords = service.ExtractKeywords("go to my API at XY", maxKeywords: 5);

        // Assert
        // Should filter out words with length <= 2
        Assert.DoesNotContain(keywords, k => k.Length <= 2);
        Assert.Contains("API", keywords, StringComparer.OrdinalIgnoreCase);
    }
}
