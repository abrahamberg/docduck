using Api.Options;
using FluentAssertions;

namespace Api.Tests.Unit;

[Trait("Category", "Unit")]
public class SearchOptionsTests
{
    [Fact]
    public void SearchOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new SearchOptions();

        // Assert
        options.DefaultTopK.Should().Be(8);
        options.MaxTopK.Should().Be(20);
        options.DefaultSearchDepth.Should().Be(3);
        options.MaxSearchDepth.Should().Be(5);
        options.EnableLexicalSearch.Should().BeTrue();
        options.LexicalScoreWeight.Should().Be(0.35);
        options.MaxLexicalResults.Should().Be(40);
        options.LexicalConfiguration.Should().Be("simple");
    }

    [Fact]
    public void SearchOptions_SetCustomValues_StoresCorrectly()
    {
        // Arrange & Act
        var options = new SearchOptions
        {
            DefaultTopK = 10,
            MaxTopK = 50,
            DefaultSearchDepth = 2,
            MaxSearchDepth = 4,
            EnableLexicalSearch = false,
            LexicalScoreWeight = 0.5,
            MaxLexicalResults = 100,
            LexicalConfiguration = "english"
        };

        // Assert
        options.DefaultTopK.Should().Be(10);
        options.MaxTopK.Should().Be(50);
        options.DefaultSearchDepth.Should().Be(2);
        options.MaxSearchDepth.Should().Be(4);
        options.EnableLexicalSearch.Should().BeFalse();
        options.LexicalScoreWeight.Should().Be(0.5);
        options.MaxLexicalResults.Should().Be(100);
        options.LexicalConfiguration.Should().Be("english");
    }

    [Fact]
    public void SearchOptions_SectionName_IsCorrect()
    {
        // Assert
        SearchOptions.SectionName.Should().Be("Search");
    }
}

[Trait("Category", "Unit")]
public class DbOptionsTests
{
    [Fact]
    public void DbOptions_SectionName_IsCorrect()
    {
        // Assert
        DbOptions.SectionName.Should().Be("Database");
    }

    [Fact]
    public void DbOptions_DefaultConnectionString_IsEmpty()
    {
        // Arrange & Act
        var options = new DbOptions();

        // Assert
        options.ConnectionString.Should().Be(string.Empty);
    }

    [Fact]
    public void DbOptions_SetConnectionString_StoresCorrectly()
    {
        // Arrange & Act
        var options = new DbOptions
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass"
        };

        // Assert
        options.ConnectionString.Should().Contain("Host=localhost");
        options.ConnectionString.Should().Contain("Database=test");
    }
}
