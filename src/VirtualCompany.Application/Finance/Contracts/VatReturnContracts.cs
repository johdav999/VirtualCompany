namespace VirtualCompany.Application.Finance;

public static class VatReturnStatusValues
{
    public const string Draft = "draft";
    public const string Calculated = "calculated";
    public const string NeedsReview = "needs_review";
    public const string Approved = "approved";
    public const string Locked = "locked";
    public const string Corrected = "corrected";
}

public static class VatReturnIssueCodes
{
    public const string MissingTaxFacts = "vat_return_missing_tax_facts";
    public const string InvalidTaxFacts = "vat_return_invalid_tax_facts";
    public const string UnsupportedBox = "vat_return_box_unsupported";
    public const string CurrencyMismatch = "vat_return_currency_mismatch";
    public const string DuplicateSource = "vat_return_duplicate_source";
    public const string PackVersionUnavailable = "vat_return_pack_version_unavailable";
    public const string PackVersionIncompatible = "vat_return_pack_version_incompatible";
    public const string ControlAccountDifference = "vat_return_control_account_difference";
    public const string FilingPeriodAmbiguous = "vat_filing_period_ambiguous";
    public const string FiscalPeriodLocked = "vat_return_fiscal_period_locked";
    public const string SourceLimitExceeded = "vat_return_source_limit_exceeded";
    public const string Stale = "vat_return_stale";
    public const string ApprovalRequired = "vat_return_approval_required";
}

public static class VatReturnAllowedActions
{
    public const string Calculate = "calculate";
    public const string Recalculate = "recalculate";
    public const string RequestApproval = "request_approval";
    public const string Finalize = "finalize";
    public const string CreateCorrection = "create_correction";
    public const string DownloadPackage = "download_package";
}

public sealed record CreateVatFilingPeriodCommand(
    Guid CompanyId, string PeriodCode, DateOnly StartDate, DateOnly EndDate,
    string Currency, Guid? FiscalPeriodId, Guid ActorUserId, DateOnly? DueDate = null);

public sealed record CalculateVatReturnCommand(
    Guid CompanyId, Guid FilingPeriodId, Guid? VatReturnId,
    string IdempotencyKey, Guid ActorUserId);
public sealed record SetVatFilingPeriodDueDateCommand(Guid CompanyId,Guid FilingPeriodId,DateOnly DueDate,Guid ActorUserId);

public sealed record RequestVatReturnApprovalCommand(
    Guid CompanyId, Guid VatReturnId, string ExpectedInputHash, Guid ActorUserId);

public sealed record FinalizeVatReturnCommand(
    Guid CompanyId, Guid VatReturnId, string ExpectedInputHash, Guid ActorUserId);

public sealed record CreateVatReturnCorrectionCommand(
    Guid CompanyId, Guid OriginalVatReturnId, string Reason,
    string EvidenceReference, string IdempotencyKey, Guid ActorUserId);

public sealed record GetVatReturnQuery(Guid CompanyId, Guid VatReturnId);
public sealed record ListVatReturnsQuery(Guid CompanyId, Guid? FilingPeriodId = null);
public sealed record GetVatReturnPackageQuery(Guid CompanyId, Guid VatReturnId);

public sealed record VatReturnPackageDownloadDto(
    string FileName, string MediaType, byte[] Content, string Checksum);

public interface IVatReturnService
{
    Task<VatFilingPeriodDto> CreateFilingPeriodAsync(CreateVatFilingPeriodCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<VatFilingPeriodDto>> ListFilingPeriodsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<VatFilingPeriodDto> SetFilingPeriodDueDateAsync(SetVatFilingPeriodDueDateCommand command, CancellationToken cancellationToken);
    Task<VatReturnDto> CalculateAsync(CalculateVatReturnCommand command, CancellationToken cancellationToken);
    Task<VatReturnDto> GetAsync(GetVatReturnQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<VatReturnDto>> ListAsync(ListVatReturnsQuery query, CancellationToken cancellationToken);
    Task<VatReturnDto> RequestApprovalAsync(RequestVatReturnApprovalCommand command, CancellationToken cancellationToken);
    Task<VatReturnDto> FinalizeAsync(FinalizeVatReturnCommand command, CancellationToken cancellationToken);
    Task<VatReturnDto> CreateCorrectionAsync(CreateVatReturnCorrectionCommand command, CancellationToken cancellationToken);
    Task<VatReturnPackageDownloadDto> DownloadPackageAsync(GetVatReturnPackageQuery query, CancellationToken cancellationToken);
}

public sealed class VatReturnOperationException : Exception
{
    public VatReturnOperationException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
