using System.IO.Compression;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class OpenXmlSalesPresentationDeckExtractor : ISalesPresentationDeckExtractor
{
    public Task<ExtractedPresentationDeck> ExtractAsync(
        Stream content,
        int maximumSlides,
        CancellationToken cancellationToken)
    {
        if (maximumSlides < 1) throw new ArgumentOutOfRangeException(nameof(maximumSlides));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (content.CanSeek) content.Position = 0;
            using var document = PresentationDocument.Open(content, false);
            var presentationPart = document.PresentationPart
                ?? throw Permanent("pptx_missing_presentation", "The PowerPoint file does not contain a presentation.");
            var presentation = presentationPart.Presentation;
            var slideIds = presentation.SlideIdList?.Elements<P.SlideId>().ToArray() ?? [];
            if (slideIds.Length == 0)
                throw Permanent("pptx_has_no_slides", "The PowerPoint file does not contain any slides.");
            if (slideIds.Length > maximumSlides)
                throw new SalesPresentationProcessingException(
                    SalesPresentationProblemCodes.SlideLimitExceeded,
                    $"The presentation contains {slideIds.Length} slides; the configured limit is {maximumSlides}.",
                    false,
                    true);

            var slides = new List<ExtractedPresentationSlide>(slideIds.Length);
            for (var index = 0; index < slideIds.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relationshipId = slideIds[index].RelationshipId?.Value;
                if (string.IsNullOrWhiteSpace(relationshipId) ||
                    presentationPart.GetPartById(relationshipId) is not SlidePart slidePart)
                    throw Permanent("pptx_slide_relationship_invalid", "A slide in the PowerPoint file is incomplete or malformed.");

                var title = ExtractTitle(slidePart);
                var text = ExtractText(slidePart.Slide.Descendants<A.Text>().Select(value => value.Text));
                var notes = slidePart.NotesSlidePart is null
                    ? null
                    : ExtractText(slidePart.NotesSlidePart.NotesSlide.Descendants<A.Text>().Select(value => value.Text));
                var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{title}\n{text}\n{notes}"));
                slides.Add(new ExtractedPresentationSlide(
                    index + 1,
                    title,
                    text,
                    string.IsNullOrWhiteSpace(notes) ? null : notes,
                    Convert.ToHexString(hashBytes).ToLowerInvariant()));
            }

            var size = presentation.SlideSize;
            return Task.FromResult(new ExtractedPresentationDeck(
                size?.Cx?.Value ?? 12_192_000L,
                size?.Cy?.Value ?? 6_858_000L,
                slides));
        }
        catch (SalesPresentationProcessingException)
        {
            throw;
        }
        catch (OpenXmlPackageException exception)
        {
            throw Permanent("pptx_malformed", "The PowerPoint file is malformed or unsupported.", exception);
        }
        catch (InvalidDataException exception)
        {
            throw Permanent("pptx_malformed", "The PowerPoint file is malformed or unsupported.", exception);
        }
        catch (FileFormatException exception)
        {
            throw Permanent("pptx_malformed", "The PowerPoint file is malformed or unsupported.", exception);
        }
    }

    private static string? ExtractTitle(SlidePart slidePart)
    {
        var titleShape = slidePart.Slide.Descendants<P.Shape>().FirstOrDefault(shape =>
        {
            var type = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?
                .GetFirstChild<P.PlaceholderShape>()?.Type?.Value;
            return type == P.PlaceholderValues.Title || type == P.PlaceholderValues.CenteredTitle;
        });
        var title = titleShape is null ? null : ExtractText(titleShape.Descendants<A.Text>().Select(value => value.Text));
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string ExtractText(IEnumerable<string> values)
    {
        var result = string.Join(
            Environment.NewLine,
            values.Select(value => value?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Take(500));
        return result[..Math.Min(result.Length, 16000)];
    }

    private static SalesPresentationProcessingException Permanent(string code, string message, Exception? inner = null) =>
        new(code, message, false, true, inner);
}
