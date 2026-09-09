namespace VirtualCompany.Application.Sales;
public sealed record SalesBrowserMeetingReadiness(bool Ready, string? Reason);
public sealed record SalesBrowserMeetingLink(string Url)
{ public override string ToString() => "SalesBrowserMeetingLink { Url = [redacted] }"; }
public interface ISalesBrowserMeetingScheduling
{
    SalesBrowserMeetingReadiness Readiness();
    void ValidateWindow(DateTime starts, DateTime ends);
    Task<string> PrepareAsync(Guid company, Guid invitation, CancellationToken ct);
    Task<SalesBrowserMeetingLink> GetLinkAsync(Guid company, Guid actor, Guid invitation, CancellationToken ct);
    Task<string> DeliveryLinkAsync(Guid company, Guid invitation, CancellationToken ct);
    Task ValidateRescheduleAsync(Guid company, Guid invitation, DateTime starts, DateTime ends, CancellationToken ct);
    Task RescheduledAsync(Guid company, Guid invitation, CancellationToken ct);
    Task RetryChangeAsync(Guid company,Guid actor,Guid invitation,Guid change,CancellationToken ct);
    Task ReconcileAsync(Guid company, Guid actor, Guid invitation, Guid? change, CancellationToken ct);
    Task CancelAsync(Guid company, Guid invitation, Guid change, CancellationToken ct);
}
