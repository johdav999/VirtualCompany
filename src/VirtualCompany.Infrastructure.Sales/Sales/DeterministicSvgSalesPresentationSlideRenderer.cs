using System.Security;
using System.Text;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class DeterministicSvgSalesPresentationSlideRenderer : ISalesPresentationSlideRenderer
{
    public Task<SalesPresentationRenderedSlide> RenderAsync(
        SalesPresentationRenderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.WidthPixels < 1 || request.HeightPixels < 1)
            throw new ArgumentOutOfRangeException(nameof(request));

        var title = string.IsNullOrWhiteSpace(request.Slide.Title)
            ? $"Slide {request.Slide.SlideNumber}"
            : request.Slide.Title.Trim();
        var bodyLines = Wrap(request.Slide.ExtractedText, 72).Take(14).ToArray();
        var builder = new StringBuilder();
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
            .Append(request.WidthPixels).Append("\" height=\"").Append(request.HeightPixels)
            .Append("\" viewBox=\"0 0 ").Append(request.WidthPixels).Append(' ').Append(request.HeightPixels)
            .Append("\"><rect width=\"100%\" height=\"100%\" fill=\"#f8fafc\"/>")
            .Append("<rect x=\"0\" y=\"0\" width=\"18\" height=\"100%\" fill=\"#2563eb\"/>")
            .Append("<text x=\"72\" y=\"110\" font-family=\"Arial, sans-serif\" font-size=\"54\" font-weight=\"700\" fill=\"#0f172a\">")
            .Append(Escape(title)).Append("</text>");
        for (var index = 0; index < bodyLines.Length; index++)
        {
            builder.Append("<text x=\"82\" y=\"").Append(200 + index * 42)
                .Append("\" font-family=\"Arial, sans-serif\" font-size=\"28\" fill=\"#334155\">")
                .Append(Escape(bodyLines[index])).Append("</text>");
        }

        builder.Append("<text x=\"").Append(request.WidthPixels - 90).Append("\" y=\"")
            .Append(request.HeightPixels - 45)
            .Append("\" text-anchor=\"end\" font-family=\"Arial, sans-serif\" font-size=\"22\" fill=\"#64748b\">")
            .Append(request.Slide.SlideNumber).Append("</text></svg>");

        return Task.FromResult(new SalesPresentationRenderedSlide(
            Encoding.UTF8.GetBytes(builder.ToString()),
            "image/svg+xml",
            ".svg",
            request.WidthPixels,
            request.HeightPixels,
            "deterministic_openxml_svg",
            "1.0",
            "flattened_or_ignored"));
    }

    private static IEnumerable<string> Wrap(string? value, int width)
    {
        if (string.IsNullOrWhiteSpace(value)) return ["No extractable slide text."];
        var words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + word.Length + 1 > width)
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
