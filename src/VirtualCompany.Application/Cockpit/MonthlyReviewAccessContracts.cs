namespace VirtualCompany.Application.Cockpit;
// Read-only access check for derived planning/approval material. Reproduction remains an explicit command.
public interface IMonthlyReviewAccessService { Task<bool> CanReadAsync(Guid company,Guid snapshot,CancellationToken ct); }
