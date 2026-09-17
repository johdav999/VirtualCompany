using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

/// <summary>
/// Renders the uploaded presentation itself instead of reconstructing a slide from extracted text.
/// PowerPoint is invoked on a dedicated STA thread because Office exposes an apartment-threaded COM API.
/// </summary>
public sealed class PowerPointSalesPresentationSlideRenderer : ISalesPresentationSlideRenderer
{
    private readonly ConcurrentDictionary<RenderKey, Lazy<Task<RenderedDeck>>> decks = new();

    public async Task<SalesPresentationRenderedSlide> RenderAsync(
        SalesPresentationRenderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.SourceContent.IsEmpty)
            throw Failure("The uploaded PowerPoint content is unavailable for visual rendering.");

        var key = new RenderKey(request.DeckId, request.ProcessingVersion, request.WidthPixels, request.HeightPixels);
        var pending = decks.GetOrAdd(key, _ => new Lazy<Task<RenderedDeck>>(
            () => RenderDeckAsync(request, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var deck = await pending.Value.WaitAsync(cancellationToken);
            if (!deck.Slides.TryGetValue(request.Slide.SlideNumber, out var content))
                throw Failure($"PowerPoint did not render slide {request.Slide.SlideNumber}.");
            return new SalesPresentationRenderedSlide(
                content, "image/png", ".png", request.WidthPixels, request.HeightPixels,
                "microsoft_powerpoint_png", deck.RendererVersion, "flattened_at_first_frame");
        }
        finally
        {
            if (request.Slide.SlideNumber == request.SlideCount ||
                pending.IsValueCreated && pending.Value.IsFaulted)
                decks.TryRemove(key, out _);
        }
    }

    private static Task<RenderedDeck> RenderDeckAsync(
        SalesPresentationRenderRequest request,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw Failure("Faithful PowerPoint rendering requires Microsoft PowerPoint on this host.");

        var completion = new TaskCompletionSource<RenderedDeck>(TaskCreationOptions.RunContinuationsAsynchronously);
        var content = request.SourceContent.ToArray();
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(RenderDeck(content, request)); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }) { IsBackground = true, Name = $"sales-presentation-render-{request.DeckId:N}" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(cancellationToken);
    }

    private static RenderedDeck RenderDeck(byte[] content, SalesPresentationRenderRequest request)
    {
        if (!OperatingSystem.IsWindows())
            throw Failure("Faithful PowerPoint rendering requires Microsoft PowerPoint on this host.");
        var powerPointType = Type.GetTypeFromProgID("PowerPoint.Application", throwOnError: false)
            ?? throw Failure("Microsoft PowerPoint is not installed on the presentation-processing host.");
        var directory = Path.Combine(Path.GetTempPath(), "VirtualCompany", "sales-presentation-render", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.pptx");
        File.WriteAllBytes(sourcePath, content);
        object? application = null;
        object? presentations = null;
        object? presentation = null;
        try
        {
            application = Activator.CreateInstance(powerPointType)
                ?? throw Failure("Microsoft PowerPoint could not be started.");
            dynamic app = application;
            presentations = app.Presentations;
            dynamic collection = presentations;
            presentation = collection.Open(sourcePath, 1, 0, 0);
            dynamic opened = presentation;
            var rendererVersion = Convert.ToString(app.Version, System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
            var actualCount = Convert.ToInt32(opened.Slides.Count, System.Globalization.CultureInfo.InvariantCulture);
            if (actualCount != request.SlideCount)
                throw Failure("PowerPoint returned a different slide count than the validated presentation package.");

            var slides = new Dictionary<int, byte[]>(actualCount);
            for (var number = 1; number <= actualCount; number++)
            {
                object? slide = null;
                try
                {
                    slide = opened.Slides.Item(number);
                    var output = Path.Combine(directory, $"slide-{number:D4}.png");
                    ((dynamic)slide).Export(output, "PNG", request.WidthPixels, request.HeightPixels);
                    slides[number] = File.ReadAllBytes(output);
                }
                finally { Release(slide); }
            }
            return new RenderedDeck(rendererVersion, slides);
        }
        catch (SalesPresentationProcessingException) { throw; }
        catch (Exception exception)
        {
            throw new SalesPresentationProcessingException(
                "powerpoint_render_failed",
                "The PowerPoint slides could not be rendered faithfully. Verify Microsoft PowerPoint on the processing host and retry.",
                true, false, exception);
        }
        finally
        {
            try { if (presentation is not null) ((dynamic)presentation).Close(); } catch { }
            try { if (application is not null) ((dynamic)application).Quit(); } catch { }
            Release(presentation);
            Release(presentations);
            Release(application);
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void Release(object? value)
    {
        if (OperatingSystem.IsWindows() && value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    private static SalesPresentationProcessingException Failure(string message) =>
        new("powerpoint_renderer_unavailable", message, true);

    private readonly record struct RenderKey(Guid DeckId, int ProcessingVersion, int Width, int Height);
    private sealed record RenderedDeck(string RendererVersion, IReadOnlyDictionary<int, byte[]> Slides);
}