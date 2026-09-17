namespace VirtualCompany.Web.Services;

public static class SalesPresentationPresetCoverEndpoint
{
    public static void MapSalesPresentationPresetCover(this WebApplication app)
    {
        app.MapGet("/sales/presentation-preset-slide/{company:guid}/{preset:guid}/{version:guid}/{slide:guid}",
            async (Guid company, Guid preset, Guid version, Guid slide, ICompanyApiTransport transport, HttpContext context, CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                using var response = await transport.SendAsync(company, HttpMethod.Get,
                    $"api/sales/presentation-presets/{preset:D}/versions/{version:D}/slides/{slide:D}/image", null, ct);
                if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
                var type = response.Content.Headers.ContentType?.MediaType;
                if (type is not ("image/png" or "image/jpeg") || response.Content.Headers.ContentLength > 10_485_760)
                    return Results.NotFound();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream();
                var chunk = new byte[16384];
                int count;
                while ((count = await source.ReadAsync(chunk, ct)) > 0)
                {
                    if (buffer.Length + count > 10_485_760) return Results.NotFound();
                    buffer.Write(chunk, 0, count);
                }
                return Results.Bytes(buffer.ToArray(), type);
            });
        app.MapGet("/sales/presentation-preset-cover/{company:guid}/{preset:guid}/{slide:guid}",
            async (Guid company, Guid preset, Guid slide, ICompanyApiTransport transport, HttpContext context, CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                using var response = await transport.SendAsync(company, HttpMethod.Get,
                    $"api/sales/presentation-presets/{preset:D}/cover/{slide:D}", null, ct);
                if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
                var type = response.Content.Headers.ContentType?.MediaType;
                if (type is not ("image/png" or "image/jpeg") || response.Content.Headers.ContentLength > 10_485_760)
                    return Results.NotFound();
                return Results.Bytes(await response.Content.ReadAsByteArrayAsync(ct), type);
            });
    }
}
