using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationDeckDomainTests
{
    [Fact]
    public void Deck_tracks_processing_failure_retry_and_completion()
    {
        var now = DateTime.UtcNow;
        var deck = CreateDeck(now);

        deck.BeginProcessing(now.AddSeconds(1), TimeSpan.FromMinutes(10));
        deck.MarkFailed("renderer_unavailable", "Rendering is temporarily unavailable.", true, false, now.AddSeconds(2));
        deck.QueueRetry(now.AddSeconds(3));
        deck.BeginProcessing(now.AddSeconds(4), TimeSpan.FromMinutes(10));
        deck.MarkProcessed(3, "renderer", "1.0", "flattened_or_ignored", 1, now.AddSeconds(5));
        deck.Activate(now.AddSeconds(6));

        Assert.Equal(SalesPresentationDeckStatus.Processed, deck.Status);
        Assert.Equal(2, deck.ProcessingAttemptCount);
        Assert.Equal(3, deck.SlideCount);
        Assert.Equal("flattened_or_ignored", deck.AnimationHandling);
        Assert.True(deck.IsActive);
        Assert.False(deck.CanRetry);
    }

    [Fact]
    public void Blocked_deck_cannot_be_retried_or_activated()
    {
        var now = DateTime.UtcNow;
        var deck = CreateDeck(now);
        deck.BeginProcessing(now.AddSeconds(1), TimeSpan.FromMinutes(10));
        deck.MarkFailed("malicious_content", "The upload was blocked by security scanning.", false, true, now.AddSeconds(2));

        Assert.Equal(SalesPresentationDeckStatus.Blocked, deck.Status);
        Assert.Throws<InvalidOperationException>(() => deck.QueueRetry(now.AddSeconds(3)));
        Assert.Throws<InvalidOperationException>(() => deck.Activate(now.AddSeconds(3)));
    }

    [Fact]
    public void Slide_preserves_exact_render_dimensions_and_allows_blank_text()
    {
        var slide = new SalesPresentationSlide(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1,
            null, string.Empty, null, "safe/slide.svg", null,
            1600, 900, 12_192_000, 6_858_000, new string('a', 64),
            "Introduce the topic", 60, "Move to the customer context", DateTime.UtcNow);

        Assert.Equal(1600, slide.ImageWidthPixels);
        Assert.Equal(900, slide.ImageHeightPixels);
        Assert.Equal(string.Empty, slide.ExtractedText);
        Assert.Equal(SalesPresentationSlideStatus.Processed, slide.Status);
    }

    private static SalesPresentationDeck CreateDeck(DateTime now) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
        "Quarterly review", "quarterly-review.pptx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        1024, new string('a', 64), "safe/deck.pptx", null, Guid.NewGuid(), now);
}
