using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;
public sealed record CustomerInvoiceFortnoxActionDto(Guid InvoiceId, Guid? CreateWriteRequestId, Guid? CreateApprovalId, string CreateStatus, Guid? BookkeepWriteRequestId, Guid? BookkeepApprovalId, string? BookkeepStatus, string Message, bool CanRequestCreate, bool CanExecuteCreate, bool CanRequestBookkeep, bool CanExecuteBookkeep, string? FortnoxInvoiceNumber, DateTime? LastSyncedUtc)
{
    public Guid InvoiceId { get; set; } = InvoiceId;
    public Guid? CreateWriteRequestId { get; set; } = CreateWriteRequestId;
    public Guid? CreateApprovalId { get; set; } = CreateApprovalId;
    public string CreateStatus { get; set; } = CreateStatus;
    public Guid? BookkeepWriteRequestId { get; set; } = BookkeepWriteRequestId;
    public Guid? BookkeepApprovalId { get; set; } = BookkeepApprovalId;
    public string? BookkeepStatus { get; set; } = BookkeepStatus;
    public string Message { get; set; } = Message;
    public bool CanRequestCreate { get; set; } = CanRequestCreate;
    public bool CanExecuteCreate { get; set; } = CanExecuteCreate;
    public bool CanRequestBookkeep { get; set; } = CanRequestBookkeep;
    public bool CanExecuteBookkeep { get; set; } = CanExecuteBookkeep;
    public string? FortnoxInvoiceNumber { get; set; } = FortnoxInvoiceNumber;
    public DateTime? LastSyncedUtc { get; set; } = LastSyncedUtc;

    public CustomerInvoiceFortnoxActionDto() : this(default !, default !, default !, string.Empty, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !)
    {
    }
}
