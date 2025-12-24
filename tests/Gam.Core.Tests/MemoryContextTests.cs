using FluentAssertions;
using Gam.Core.Models;
using Xunit;

namespace Gam.Core.Tests;

public class MemoryContextTests
{
    [Fact]
    public void FormatForPrompt_WithNoPages_ShouldReturnEmpty()
    {
        // Arrange
        var context = MemoryContext.Empty;

        // Act
        var result = context.FormatForPrompt();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void FormatForPrompt_WithPages_ShouldFormatCorrectly()
    {
        // Arrange
        var context = new MemoryContext
        {
            Pages = new List<RetrievedPage>
            {
                new()
                {
                    PageId = Guid.NewGuid(),
                    Content = "This is memory content",
                    TokenCount = 10,
                    RelevanceScore = 0.9f,
                    RetrievedBy = "vector",
                    CreatedAt = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)
                }
            },
            TotalTokens = 10,
            IterationsPerformed = 1,
            Duration = TimeSpan.FromMilliseconds(100)
        };

        // Act
        var result = context.FormatForPrompt();

        // Assert
        result.Should().Contain("# Relevant Memory Context");
        result.Should().Contain("## Memory from 2024-01-15");
        result.Should().Contain("This is memory content");
    }

    [Fact]
    public void Empty_ShouldHaveCorrectDefaults()
    {
        // Act
        var empty = MemoryContext.Empty;

        // Assert
        empty.Pages.Should().BeEmpty();
        empty.TotalTokens.Should().Be(0);
        empty.IterationsPerformed.Should().Be(0);
        empty.Duration.Should().Be(TimeSpan.Zero);
    }
}
