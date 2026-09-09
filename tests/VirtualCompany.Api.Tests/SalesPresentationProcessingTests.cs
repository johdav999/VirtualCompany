using System.Text;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationProcessingTests
{
    [Fact]
    public async Task Renderer_produces_deterministic_exact_size_safe_svg()
    {
        var renderer = new DeterministicSvgSalesPresentationSlideRenderer();
        var request = new SalesPresentationRenderRequest(
            Guid.NewGuid(), Guid.NewGuid(), 1,
            new ExtractedPresentationSlide(2, "Revenue < plan", "Use & validate customer data", null, new string('a', 64)),
            1600, 900);

        var first = await renderer.RenderAsync(request, CancellationToken.None);
        var second = await renderer.RenderAsync(request, CancellationToken.None);
        var svg = Encoding.UTF8.GetString(first.Content);

        Assert.Equal(first.Content, second.Content);
        Assert.Equal(1600, first.WidthPixels);
        Assert.Equal(900, first.HeightPixels);
        Assert.Equal("flattened_or_ignored", first.AnimationHandling);
        Assert.Contains("width=\"1600\" height=\"900\"", svg, StringComparison.Ordinal);
        Assert.Contains("Revenue &lt; plan", svg, StringComparison.Ordinal);
        Assert.Contains("Use &amp; validate", svg, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extractor_rejects_malformed_powerpoint_with_safe_failure()
    {
        var extractor = new OpenXmlSalesPresentationDeckExtractor();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("not a presentation"));

        var exception = await Assert.ThrowsAsync<SalesPresentationProcessingException>(() =>
            extractor.ExtractAsync(content, 200, CancellationToken.None));

        Assert.True(exception.Blocked);
        Assert.False(exception.CanRetry);
        Assert.DoesNotContain("PK", exception.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extractor_preserves_slide_order_text_notes_blank_slides_and_source_dimensions()
    {
        await using var content = CreatePresentation();
        var result = await new OpenXmlSalesPresentationDeckExtractor()
            .ExtractAsync(content, 10, CancellationToken.None);

        Assert.Equal(12_192_000L, result.SourceWidthEmus);
        Assert.Equal(6_858_000L, result.SourceHeightEmus);
        Assert.Collection(result.Slides,
            first =>
            {
                Assert.Equal(1, first.SlideNumber);
                Assert.Equal("Opening", first.Title);
                Assert.Contains("Customer outcome", first.ExtractedText, StringComparison.Ordinal);
                Assert.Equal("Ask about timing", first.SpeakerNotes);
            },
            second =>
            {
                Assert.Equal(2, second.SlideNumber);
                Assert.Equal(string.Empty, second.ExtractedText);
                Assert.Null(second.SpeakerNotes);
            });
    }

    [Fact]
    public void Structured_slide_plan_accepts_only_supplied_sources_and_flags_unsupported_claims()
    {
        const string slideSource = "presentation-slide:0123456789abcdef0123456789abcdef:1";
        const string knowledgeSource = "knowledge-chunk:0123456789abcdef0123456789abcdef";
        var structured = JsonNode.Parse($$"""
            {"resultVersion":"1.0.0","state":"ready","safeExplanation":"Plan ready.","slides":[
              {"slideSourceId":"{{slideSource}}","objective":"Explain the outcome","talkingPoints":["Start with context"],
               "expectedTimingSeconds":75,"transition":"Invite a response","claims":[
                 {"text":"Supported statement","type":"confirmed_fact","sourceIds":["{{slideSource}}","{{knowledgeSource}}"]},
                 {"text":"Invented source","type":"confirmed_fact","sourceIds":["{{slideSource}}","knowledge-chunk:not-approved"]}
               ]}
            ]}
            """)!.AsObject();
        var runId = Guid.NewGuid();
        var result = new AgentReasoningResult(
            runId, AgentAiRunStatuses.Completed, "1.0.0", "Plan ready.", [], 1m, [], [], [], [],
            StructuredResult: structured);

        var plans = SalesPresentationDeckProcessor.ParseSlidePlans(
            result, new HashSet<string>([slideSource, knowledgeSource], StringComparer.Ordinal));

        var plan = Assert.Single(plans);
        Assert.Equal(75, plan.ExpectedTimingSeconds);
        Assert.Equal(runId, plan.RunId);
        Assert.Equal(knowledgeSource, plan.Claims[0].SourceIds[1]);
        Assert.Single(plan.Claims[1].SourceIds);
        Assert.Equal(SalesMeetingArtifactClassification.ConfirmedFact,
            SalesPresentationDeckProcessor.ClassifyClaim(plan.Claims[0].Type, hasApprovedKnowledgeSource: true));
        Assert.Equal(SalesMeetingArtifactClassification.NeedsConfirmation,
            SalesPresentationDeckProcessor.ClassifyClaim(plan.Claims[1].Type, hasApprovedKnowledgeSource: false));
    }

    private static MemoryStream CreatePresentation()
    {
        var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation(
                new P.SlideIdList(),
                new P.SlideSize { Cx = 12_192_000, Cy = 6_858_000 });
            AddSlide(presentationPart, 256U, "Opening", "Customer outcome", "Ask about timing");
            AddSlide(presentationPart, 257U, null, null, null);
            presentationPart.Presentation.Save();
        }
        stream.Position = 0;
        return stream;
    }

    private static void AddSlide(
        PresentationPart presentationPart,
        uint id,
        string? title,
        string? body,
        string? notes)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();
        var shapes = new List<P.Shape>();
        if (title is not null) shapes.Add(TextShape(2U, "Title", title, P.PlaceholderValues.Title));
        if (body is not null) shapes.Add(TextShape(3U, "Body", body, P.PlaceholderValues.Body));
        slidePart.Slide = new P.Slide(new P.CommonSlideData(ShapeTree(shapes)));
        slidePart.Slide.Save();
        if (notes is not null)
        {
            var notesPart = slidePart.AddNewPart<NotesSlidePart>();
            notesPart.NotesSlide = new P.NotesSlide(new P.CommonSlideData(ShapeTree([
                TextShape(2U, "Notes", notes, P.PlaceholderValues.Body)
            ])));
            notesPart.NotesSlide.Save();
        }

        presentationPart.Presentation.SlideIdList!.Append(new P.SlideId
        {
            Id = id,
            RelationshipId = presentationPart.GetIdOfPart(slidePart)
        });
    }

    private static P.ShapeTree ShapeTree(IEnumerable<P.Shape> shapes)
    {
        var tree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new A.TransformGroup()));
        foreach (var shape in shapes) tree.Append(shape);
        return tree;
    }

    private static P.Shape TextShape(uint id, string name, string text, P.PlaceholderValues placeholder) =>
        new(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = placeholder })),
            new P.ShapeProperties(),
            new P.TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Run(new A.Text(text)))));
}
