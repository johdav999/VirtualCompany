using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FixedAssetService : IFixedAssetService
{
    private const string SourceType = "fixed_asset_book_event";
    private readonly VirtualCompanyDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _clock;
    private readonly FixedAssetTelemetry _telemetry;

    public FixedAssetService(VirtualCompanyDbContext db, IAccountingPostingService posting,
        IAuditEventWriter audit, TimeProvider clock, FixedAssetTelemetry telemetry)
    { _db = db; _posting = posting; _audit = audit; _clock = clock; _telemetry = telemetry; }

    public async Task<IReadOnlyList<FixedAssetClassDto>> ListClassesAsync(Guid companyId, CancellationToken ct) =>
        (await _db.FixedAssetClasses.AsNoTracking().Where(x => x.CompanyId == Required(companyId, nameof(companyId)))
            .OrderBy(x => x.Code).ToListAsync(ct)).Select(MapClass).ToArray();

    public async Task<FixedAssetClassDto> SaveClassAsync(SaveFixedAssetClassCommand command, CancellationToken ct)
    {
        Required(command.CompanyId, nameof(command.CompanyId)); Required(command.ActorUserId, nameof(command.ActorUserId));
        await ValidateClassAccountsAsync(command.CompanyId, command.Class, ct);
        var now = Now(); FixedAssetClass entity;
        if (command.ClassId.HasValue)
        {
            entity = await _db.FixedAssetClasses.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ClassId, ct)
                ?? throw Error(FixedAssetReasonCodes.ClassNotFound, "The fixed-asset class was not found.");
            EnsureVersion(entity.Version, command.ExpectedVersion);
            entity.Update(command.Class.Code, command.Class.Name, command.Class.BookMethod, command.Class.UsefulLifeMonths,
                command.Class.DefaultResidualPercent, command.Class.CostAccountId, command.Class.AccumulatedDepreciationAccountId,
                command.Class.DepreciationExpenseAccountId, command.Class.AccumulatedImpairmentAccountId,
                command.Class.ImpairmentExpenseAccountId, command.Class.DisposalGainAccountId,
                command.Class.DisposalLossAccountId, command.Class.VoucherSeriesCode, command.Class.RequiresApproval,
                command.ActorUserId, now);
        }
        else
        {
            entity = new(Guid.NewGuid(), command.CompanyId, command.Class.Code, command.Class.Name,
                command.Class.BookMethod, command.Class.UsefulLifeMonths, command.Class.DefaultResidualPercent,
                command.Class.CostAccountId, command.Class.AccumulatedDepreciationAccountId,
                command.Class.DepreciationExpenseAccountId, command.Class.AccumulatedImpairmentAccountId,
                command.Class.ImpairmentExpenseAccountId, command.Class.DisposalGainAccountId,
                command.Class.DisposalLossAccountId, command.Class.VoucherSeriesCode,
                command.Class.RequiresApproval, command.ActorUserId, now);
            _db.FixedAssetClasses.Add(entity);
        }
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.FixedAssetClassConfigured,
            AuditTargetTypes.FixedAssetClass, entity.Id, "Configured a versioned fixed-asset book class.", null, now, ct);
        await SaveAsync(ct); return MapClass(entity);
    }

    public async Task<FixedAssetRegistrationPreviewDto> PreviewRegistrationAsync(
        PreviewFixedAssetRegistrationQuery query, CancellationToken ct)
    {
        Required(query.CompanyId, nameof(query.CompanyId)); Required(query.ActorUserId, nameof(query.ActorUserId));
        if (string.IsNullOrWhiteSpace(query.Asset.SourceType) || string.IsNullOrWhiteSpace(query.Asset.SourceId) ||
            string.IsNullOrWhiteSpace(query.Asset.SourceVersion))
            throw Error(FixedAssetReasonCodes.SourceConflict, "A source type, source identity, and source version are required.");
        var existing = await _db.FixedAssetRegisterItems.AsNoTracking().Include(x => x.AssetClass)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId &&
                x.SourceType == query.Asset.SourceType.Trim().ToLower() && x.SourceId == query.Asset.SourceId.Trim() &&
                x.SourceVersion == query.Asset.SourceVersion.Trim(), ct);
        var assetClass = await ClassAsync(query.CompanyId, query.Asset.AssetClassId, ct);
        if (!assetClass.IsActive) throw Error(FixedAssetReasonCodes.ClassInactive, "The selected asset class is inactive.");
        var baseCurrency = await _db.AccountingConfigurations.AsNoTracking().Where(x => x.CompanyId == query.CompanyId)
            .Select(x => x.BaseCurrency).SingleOrDefaultAsync(ct)
            ?? throw Error(FixedAssetReasonCodes.InvalidState, "Accounting configuration is required.");
        if (!string.Equals(baseCurrency, query.Asset.Currency, StringComparison.OrdinalIgnoreCase))
            throw Error(FixedAssetReasonCodes.InvalidAmount, "Fixed-asset book accounting currently requires functional currency. Foreign-currency acquisition evidence remains on the source document.");
        await ValidateSourceAsync(query.CompanyId, query.Asset.SourceDocumentId, query.Asset.LegacyFinanceAssetId, ct);
        var residual = query.Asset.ResidualValue ?? Money(query.Asset.AcquisitionCost * assetClass.DefaultResidualPercent / 100m);
        var usefulLife = query.Asset.UsefulLifeMonths ?? assetClass.UsefulLifeMonths;
        _ = BuildTransientRegistration(query.CompanyId, query.Asset, assetClass, residual, usefulLife, query.ActorUserId);
        var checksum = Hash(JsonSerializer.Serialize(new
        {
            query.CompanyId, Asset = query.Asset, AssetClassVersion = assetClass.Version,
            assetClass.DefinitionHash, ResidualValue = residual, UsefulLifeMonths = usefulLife
        }));
        return new(query.Asset, MapClass(assetClass), residual, usefulLife, assetClass.BookMethod,
            checksum, IsRegistered: existing is not null, assetClass.RequiresApproval, existing?.Id);
    }

    public async Task<FixedAssetDto> RegisterAsync(RegisterFixedAssetCommand command, CancellationToken ct)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var existing = await _db.FixedAssetRegisterItems.Include(x => x.AssetClass).Include(x => x.Events)
            .Include(x => x.Components)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SourceType == command.Asset.SourceType.ToLower() &&
                x.SourceId == command.Asset.SourceId && x.SourceVersion == command.Asset.SourceVersion, ct);
        if (existing is not null) return Map(existing);
        var assetClass = await ClassAsync(command.CompanyId, command.Asset.AssetClassId, ct);
        if (!assetClass.IsActive) throw Error(FixedAssetReasonCodes.ClassInactive, "The selected asset class is inactive.");
        var baseCurrency = await _db.AccountingConfigurations.AsNoTracking().Where(x => x.CompanyId == command.CompanyId)
            .Select(x => x.BaseCurrency).SingleOrDefaultAsync(ct) ?? throw Error(FixedAssetReasonCodes.InvalidState, "Accounting configuration is required.");
        if (!string.Equals(baseCurrency, command.Asset.Currency, StringComparison.OrdinalIgnoreCase))
            throw Error(FixedAssetReasonCodes.InvalidAmount, "Fixed-asset book accounting currently requires functional currency. Foreign-currency acquisition evidence remains on the source document.");
        await ValidateSourceAsync(command.CompanyId, command.Asset.SourceDocumentId, command.Asset.LegacyFinanceAssetId, ct);
        var residual = command.Asset.ResidualValue ?? decimal.Round(command.Asset.AcquisitionCost * assetClass.DefaultResidualPercent / 100m, 2, MidpointRounding.AwayFromZero);
        var now = Now(); var item = new FixedAssetRegisterItem(Guid.NewGuid(), command.CompanyId, assetClass.Id,
            assetClass.Version, assetClass.DefinitionHash, command.Asset.AssetNumber, command.Asset.Name,
            command.Asset.Currency, command.Asset.AcquisitionCost, residual,
            command.Asset.UsefulLifeMonths ?? assetClass.UsefulLifeMonths, assetClass.BookMethod,
            command.Asset.AcquisitionDate, command.Asset.SourceType, command.Asset.SourceId,
            command.Asset.SourceVersion, command.Asset.SourceDocumentId, command.Asset.LegacyFinanceAssetId,
            command.Asset.Custodian, command.Asset.Location, Dimensions(command.Asset.DimensionFacts),
            command.ActorUserId, now);
        var components = command.Asset.Components ?? [];
        if (components.Count > 25)
            throw Error(FixedAssetReasonCodes.InvalidAmount, "An asset can retain at most 25 book components.");
        if (components.Select(x => x.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != components.Count)
            throw Error(FixedAssetReasonCodes.InvalidAmount, "Component codes must be unique within an asset.");
        if (Money(components.Sum(x => x.Cost)) > item.AcquisitionCost ||
            Money(components.Sum(x => x.ResidualValue)) > item.ResidualValue)
            throw Error(FixedAssetReasonCodes.InvalidAmount, "Component cost and residual totals must fit within the retained asset totals.");
        foreach (var component in components)
        {
            if (component.PlacedInServiceDate < item.AcquisitionDate)
                throw Error(FixedAssetReasonCodes.InvalidState, "A component cannot be placed in service before acquisition.");
            item.Components.Add(new(Guid.NewGuid(), command.CompanyId, item.Id, component.Code,
                component.Name, component.Cost, component.ResidualValue, component.UsefulLifeMonths,
                component.PlacedInServiceDate));
        }
        _db.FixedAssetRegisterItems.Add(item);
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.FixedAssetRegistered,
            AuditTargetTypes.FixedAsset, item.Id, "Registered a fixed asset without inventing capitalization or depreciation history.", command.CorrelationId, now, ct);
        await SaveAsync(ct); _telemetry.Registered(); return await GetAsync(new(command.CompanyId, item.Id), ct);
    }

    public async Task<FixedAssetDto> GetAsync(GetFixedAssetQuery query, CancellationToken ct) =>
        Map(await AssetAsync(query.CompanyId, query.AssetId, ct));

    public async Task<FixedAssetListDto> ListAsync(ListFixedAssetsQuery query, CancellationToken ct)
    {
        var source = _db.FixedAssetRegisterItems.AsNoTracking().Include(x => x.AssetClass).Include(x => x.Events)
            .Include(x => x.Components)
            .Where(x => x.CompanyId == Required(query.CompanyId, nameof(query.CompanyId)));
        if (!string.IsNullOrWhiteSpace(query.Status)) source = source.Where(x => x.Status == query.Status.Trim().ToLower());
        if (query.AssetClassId.HasValue) source = source.Where(x => x.AssetClassId == query.AssetClassId);
        if (!string.IsNullOrWhiteSpace(query.Search)) { var q = query.Search.Trim(); source = source.Where(x => x.AssetNumber.Contains(q) || x.Name.Contains(q)); }
        var total = await source.CountAsync(ct); var take = Math.Clamp(query.Take, 1, 500); var skip = Math.Max(0, query.Skip);
        var items = await source.OrderBy(x => x.AssetNumber).Skip(skip).Take(take).ToListAsync(ct);
        var all = await _db.FixedAssetRegisterItems.AsNoTracking().Where(x => x.CompanyId == query.CompanyId).ToListAsync(ct);
        var conflicts = await _db.FixedAssetMigrationConflicts.CountAsync(x => x.CompanyId == query.CompanyId && x.Status == "open", ct);
        return new(items.Select(Map).ToArray(), total, skip, take, Money(all.Sum(x => x.GrossBookValue)),
            Money(all.Sum(x => x.AccumulatedDepreciation)), Money(all.Sum(x => x.AccumulatedImpairment)),
            Money(all.Sum(x => x.NetBookValue)), conflicts);
    }

    public async Task<FixedAssetDto> CapitalizeAsync(FixedAssetLifecycleCommand command, CancellationToken ct)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var asset = await AssetAsync(command.CompanyId, command.AssetId, ct);
        if (await EventAsync(command.CompanyId, command.IdempotencyKey, ct) is { } replay)
        {
            EnsureEventReplay(replay, asset.Id, FixedAssetEventTypes.Capitalization, command.EffectiveDate,
                command.SourceVersion, command.Amount);
            return Map(asset);
        }
        EnsureVersion(asset.Version, command.ExpectedVersion);
        if (Money(command.Amount) != asset.AcquisitionCost) throw Error(FixedAssetReasonCodes.InvalidAmount, "Capitalization must equal the retained acquisition cost.");
        asset.EnsureCanCapitalize(command.EffectiveDate);
        var cls = asset.AssetClass; await ValidateOffsetAsync(command.CompanyId, command.OffsetAccountId, ct);
        await using var transaction = await BeginAtomicWriteAsync(ct);
        var posted = await PostAsync(asset, command.FiscalPeriodId, command.EffectiveDate, cls.VoucherSeriesCode,
            "fixed_asset_capitalization", "Capitalize fixed asset", command.SourceVersion, command.IdempotencyKey,
            command.ActorUserId, command.CorrelationId,
            [Line(cls.CostAccountId, command.Amount, 0m, asset, "Fixed-asset cost"), Line(command.OffsetAccountId, 0m, command.Amount, asset, "Capitalization source")], ct);
        if (!posted.Replay)
        {
            var now = Now(); asset.MarkCapitalized(command.EffectiveDate, now);
            AddEvent(asset, FixedAssetEventTypes.Capitalization, command.EffectiveDate, command.Amount,
                command.Amount, 0m, 0m, 0m, 0m, command.SourceVersion, command.IdempotencyKey,
                command.FiscalPeriodId, posted.JournalId, null, null, command.ActorUserId, now);
            await LifecycleAuditAsync(command.CompanyId, command.ActorUserId, asset.Id, "Capitalized a fixed asset through the native posting authority.", command.CorrelationId, now, ct);
            await SaveAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await GetAsync(new(command.CompanyId, asset.Id), ct);
    }

    public async Task<FixedAssetDto> PlaceInServiceAsync(PlaceFixedAssetInServiceCommand command, CancellationToken ct)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var asset = await AssetAsync(command.CompanyId, command.AssetId, ct);
        var sourceVersion = $"placed-in-service:{command.PlacedInServiceDate:yyyyMMdd}";
        if (await EventAsync(command.CompanyId, command.IdempotencyKey, ct) is { } replay)
        {
            EnsureEventReplay(replay, asset.Id, FixedAssetEventTypes.PlacedInService,
                command.PlacedInServiceDate, sourceVersion, 0m);
            return Map(asset);
        }
        EnsureVersion(asset.Version, command.ExpectedVersion);
        var now = Now(); asset.PlaceInService(command.PlacedInServiceDate, now);
        AddEvent(asset, FixedAssetEventTypes.PlacedInService, command.PlacedInServiceDate, 0m, 0m, 0m, 0m, 0m, 0m,
            sourceVersion, command.IdempotencyKey, null, null, null, null, command.ActorUserId, now);
        await LifecycleAuditAsync(command.CompanyId, command.ActorUserId, asset.Id, "Placed a capitalized asset in service.", command.CorrelationId, now, ct);
        await SaveAsync(ct); return Map(asset);
    }

    public Task<FixedAssetDto> ImproveAsync(FixedAssetLifecycleCommand command, CancellationToken ct) =>
        PostAmountLifecycleAsync(command, FixedAssetEventTypes.Improvement, "fixed_asset_improvement", "Post fixed-asset improvement",
            (a, c) => [Line(a.AssetClass.CostAccountId, c.Amount, 0m, a, "Asset improvement"), Line(c.OffsetAccountId, 0m, c.Amount, a, "Improvement source")],
            (a, now) => a.ApplyImprovement(command.Amount, now), command.Amount, 0m, ct);

    public Task<FixedAssetDto> ImpairAsync(FixedAssetLifecycleCommand command, CancellationToken ct) =>
        PostAmountLifecycleAsync(command, FixedAssetEventTypes.Impairment, "fixed_asset_impairment", "Post fixed-asset impairment",
            (a, c) => [Line(a.AssetClass.ImpairmentExpenseAccountId, c.Amount, 0m, a, "Impairment expense"), Line(a.AssetClass.AccumulatedImpairmentAccountId, 0m, c.Amount, a, "Accumulated impairment")],
            (a, now) => a.ApplyImpairment(command.Amount, now), 0m, command.Amount, ct);

    public async Task<FixedAssetDto> TransferAsync(TransferFixedAssetCommand command, CancellationToken ct)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var asset = await AssetAsync(command.CompanyId, command.AssetId, ct);
        var dimensionSnapshot = Dimensions(command.DimensionFacts);
        var sourceVersion = Hash(JsonSerializer.Serialize(new
        {
            Custodian = command.Custodian?.Trim(), Location = command.Location?.Trim(),
            Dimensions = dimensionSnapshot
        }));
        if (await EventAsync(command.CompanyId, command.IdempotencyKey, ct) is { } replay)
        {
            EnsureEventReplay(replay, asset.Id, FixedAssetEventTypes.Transfer, replay.EffectiveDate,
                sourceVersion, 0m);
            return Map(asset);
        }
        EnsureVersion(asset.Version, command.ExpectedVersion);
        var now = Now(); asset.Transfer(command.Custodian, command.Location, Dimensions(command.DimensionFacts), now);
        AddEvent(asset, FixedAssetEventTypes.Transfer, DateOnly.FromDateTime(now), 0m, 0m, 0m, 0m, 0m, 0m,
            sourceVersion, command.IdempotencyKey, null, null, null, null, command.ActorUserId, now);
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.FixedAssetTransferred,
            AuditTargetTypes.FixedAsset, asset.Id, "Transferred asset custody, location, or dimension assignment without changing posted history.", command.CorrelationId, now, ct);
        await SaveAsync(ct); return Map(asset);
    }

    public async Task<FixedAssetDisposalPreviewDto> PreviewDisposalAsync(
        PreviewFixedAssetDisposalQuery query, CancellationToken ct)
    {
        Required(query.CompanyId, nameof(query.CompanyId)); Required(query.ActorUserId, nameof(query.ActorUserId));
        if (string.IsNullOrWhiteSpace(query.SourceVersion))
            throw Error(FixedAssetReasonCodes.SourceConflict, "A current disposal source version is required.");
        var asset = await AssetAsync(query.CompanyId, query.AssetId, ct);
        EnsureVersion(asset.Version, query.ExpectedVersion);
        if (asset.Status == FixedAssetStatuses.Disposed)
            throw Error(FixedAssetReasonCodes.InvalidState, "The asset is already disposed.", true);
        asset.EnsureCanDispose(query.DisposalDate, query.Proceeds);
        await ValidateOffsetAsync(query.CompanyId, query.ProceedsAccountId, ct);
        await ValidatePeriodAsync(query.CompanyId, query.FiscalPeriodId, query.DisposalDate, ct);
        var nbv = asset.NetBookValue;
        var gainLoss = Money(query.Proceeds - nbv);
        var cls = asset.AssetClass;
        var lines = new List<ProposedAccountingLine>();
        if (query.Proceeds > 0m) lines.Add(Line(query.ProceedsAccountId, query.Proceeds, 0m, asset, "Disposal proceeds"));
        if (asset.AccumulatedDepreciation > 0m) lines.Add(Line(cls.AccumulatedDepreciationAccountId, asset.AccumulatedDepreciation, 0m, asset, "Remove accumulated depreciation"));
        if (asset.AccumulatedImpairment > 0m) lines.Add(Line(cls.AccumulatedImpairmentAccountId, asset.AccumulatedImpairment, 0m, asset, "Remove accumulated impairment"));
        if (gainLoss < 0m) lines.Add(Line(cls.DisposalLossAccountId, -gainLoss, 0m, asset, "Loss on disposal"));
        lines.Add(Line(cls.CostAccountId, 0m, asset.GrossBookValue, asset, "Remove fixed-asset cost"));
        if (gainLoss > 0m) lines.Add(Line(cls.DisposalGainAccountId, 0m, gainLoss, asset, "Gain on disposal"));
        var checksum = Hash(JsonSerializer.Serialize(new
        {
            query.CompanyId, query.AssetId, query.DisposalDate, query.FiscalPeriodId,
            query.ProceedsAccountId, query.Proceeds, query.ExpectedVersion, query.SourceVersion,
            AssetClassVersion = asset.AssetClassVersion, asset.AssetClassHash, Lines = lines
        }));
        var proposed = new ProposedAccountingEntry(query.CompanyId, query.FiscalPeriodId,
            cls.VoucherSeriesCode, query.DisposalDate, query.DisposalDate, "fixed_asset_disposal",
            "Dispose fixed asset", SourceType, asset.Id.ToString("D"), query.SourceVersion,
            $"preview:fixed-asset-disposal:{checksum}", lines, query.ActorUserId,
            RequiresApproval: cls.RequiresApproval,
            PolicyFacts: new Dictionary<string, string>
            {
                ["assetId"] = asset.Id.ToString("D"), ["assetClassVersion"] = asset.AssetClassVersion.ToString(),
                ["assetClassHash"] = asset.AssetClassHash, ["bookMethod"] = asset.BookMethod,
                ["classRequiresApproval"] = cls.RequiresApproval.ToString().ToLowerInvariant(),
                ["taxRegisterSupported"] = "false"
            });
        var postingPreview = await _posting.PreviewAsync(new(proposed), ct);
        return new(Map(asset), query.DisposalDate, query.FiscalPeriodId, nbv, query.Proceeds,
            gainLoss, postingPreview, checksum, IsPosted: false);
    }

    public async Task<FixedAssetDto> DisposeAsync(DisposeFixedAssetCommand command, CancellationToken ct)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var asset = await AssetAsync(command.CompanyId, command.AssetId, ct);
        if (await EventAsync(command.CompanyId, command.IdempotencyKey, ct) is { } replay)
        {
            EnsureEventReplay(replay, asset.Id, FixedAssetEventTypes.Disposal, command.DisposalDate,
                command.SourceVersion, proceeds: command.Proceeds);
            return Map(asset);
        }
        EnsureVersion(asset.Version, command.ExpectedVersion);
        if (asset.Status == FixedAssetStatuses.Disposed) throw Error(FixedAssetReasonCodes.InvalidState, "The asset is already disposed.", true);
        asset.EnsureCanDispose(command.DisposalDate, command.Proceeds);
        await ValidateOffsetAsync(command.CompanyId, command.ProceedsAccountId, ct);
        var nbv = asset.NetBookValue; var gainLoss = Money(command.Proceeds - nbv); var cls = asset.AssetClass;
        var lines = new List<ProposedAccountingLine>();
        if (command.Proceeds > 0m) lines.Add(Line(command.ProceedsAccountId, command.Proceeds, 0m, asset, "Disposal proceeds"));
        if (asset.AccumulatedDepreciation > 0m) lines.Add(Line(cls.AccumulatedDepreciationAccountId, asset.AccumulatedDepreciation, 0m, asset, "Remove accumulated depreciation"));
        if (asset.AccumulatedImpairment > 0m) lines.Add(Line(cls.AccumulatedImpairmentAccountId, asset.AccumulatedImpairment, 0m, asset, "Remove accumulated impairment"));
        if (gainLoss < 0m) lines.Add(Line(cls.DisposalLossAccountId, -gainLoss, 0m, asset, "Loss on disposal"));
        lines.Add(Line(cls.CostAccountId, 0m, asset.GrossBookValue, asset, "Remove fixed-asset cost"));
        if (gainLoss > 0m) lines.Add(Line(cls.DisposalGainAccountId, 0m, gainLoss, asset, "Gain on disposal"));
        await using var transaction = await BeginAtomicWriteAsync(ct);
        var posted = await PostAsync(asset, command.FiscalPeriodId, command.DisposalDate, cls.VoucherSeriesCode,
            "fixed_asset_disposal", "Dispose fixed asset", command.SourceVersion, command.IdempotencyKey,
            command.ActorUserId, command.CorrelationId, lines, ct);
        if (!posted.Replay)
        {
            var now = Now(); asset.Dispose(command.DisposalDate, command.Proceeds, gainLoss, now);
            AddEvent(asset, FixedAssetEventTypes.Disposal, command.DisposalDate, nbv, -asset.GrossBookValue,
                -asset.AccumulatedDepreciation, -asset.AccumulatedImpairment, command.Proceeds, gainLoss,
                command.SourceVersion, command.IdempotencyKey, command.FiscalPeriodId, posted.JournalId,
                null, null, command.ActorUserId, now);
            await LifecycleAuditAsync(command.CompanyId, command.ActorUserId, asset.Id, "Disposed a fixed asset and reconciled cost, accumulated balances, proceeds, and gain or loss.", command.CorrelationId, now, ct);
            await SaveAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await GetAsync(new(command.CompanyId, asset.Id), ct);
    }

    public async Task<FixedAssetDto> ReverseEventAsync(ReverseFixedAssetEventCommand command, CancellationToken ct)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var asset = await AssetAsync(command.CompanyId, command.AssetId, ct);
        if (await EventAsync(command.CompanyId, command.IdempotencyKey, ct) is { } replay)
        {
            EnsureEventReplay(replay, asset.Id, FixedAssetEventTypes.Reversal, command.PostingDate,
                command.SourceVersion, originalEventId: command.EventId);
            return Map(asset);
        }
        EnsureVersion(asset.Version, command.ExpectedVersion);
        var original = asset.Events.SingleOrDefault(x => x.Id == command.EventId)
            ?? throw Error(FixedAssetReasonCodes.NotFound, "The fixed-asset event was not found.");
        if (!original.LedgerEntryId.HasValue || original.Status != "posted") throw Error(FixedAssetReasonCodes.InvalidState, "Only a posted, unreversed book event can be reversed.");
        await using var transaction = await BeginAtomicWriteAsync(ct);
        var reversed = await _posting.ReverseAsync(new(command.CompanyId, original.LedgerEntryId.Value,
            command.FiscalPeriodId, asset.AssetClass.VoucherSeriesCode, command.PostingDate, command.Reason,
            command.SourceVersion, $"fixed-asset-reversal:{command.IdempotencyKey}", command.ActorUserId,
            CorrelationId: command.CorrelationId), ct);
        if (!reversed.IsIdempotentReplay)
        {
            var now = Now();
            if (original.EventType == FixedAssetEventTypes.Depreciation)
            {
                var allocations = ParseComponentAllocations(original.ComponentAllocationJson);
                var previousThrough = asset.Events.Where(x => x.Id != original.Id &&
                        x.EventType == FixedAssetEventTypes.Depreciation && x.Status == "posted")
                    .Select(x => (DateOnly?)x.EffectiveDate).OrderByDescending(x => x).FirstOrDefault();
                asset.ReverseDepreciation(original.DepreciationMovement, allocations,
                    previousThrough, now);
            }
            else if (original.EventType == FixedAssetEventTypes.Impairment) asset.ReverseImpairment(original.ImpairmentMovement, now);
            else if (original.EventType == FixedAssetEventTypes.Disposal) asset.ReverseDisposal(now);
            else throw Error(FixedAssetReasonCodes.InvalidState, "This lifecycle event requires an explicit adjustment rather than state reversal.");
            original.MarkReversed();
            AddEvent(asset, FixedAssetEventTypes.Reversal, command.PostingDate, -original.Amount,
                -original.CostMovement, -original.DepreciationMovement, -original.ImpairmentMovement,
                -original.Proceeds, -original.GainLoss, command.SourceVersion, command.IdempotencyKey,
                command.FiscalPeriodId, reversed.Journal.Id, null, original.Id, command.ActorUserId, now);
            await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.FixedAssetEventReversed,
                AuditTargetTypes.FixedAsset, asset.Id, "Reversed a posted fixed-asset event while retaining the original evidence.", command.CorrelationId, now, ct);
            await SaveAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await GetAsync(new(command.CompanyId, asset.Id), ct);
    }

    public async Task<FixedAssetDepreciationPreviewDto> PreviewDepreciationAsync(PreviewFixedAssetDepreciationQuery query, CancellationToken ct)
    {
        var assets = await _db.FixedAssetRegisterItems.AsNoTracking().Include(x => x.AssetClass)
            .Include(x => x.Components)
            .Where(x => x.CompanyId == Required(query.CompanyId, nameof(query.CompanyId)) &&
                (x.Status == FixedAssetStatuses.InService || x.Status == FixedAssetStatuses.Impaired)).OrderBy(x => x.AssetNumber).ToListAsync(ct);
        var items = assets.Select(x => { var c = FixedAssetDepreciationCalculator.Calculate(x, query.PeriodStart, query.PeriodEnd); return new FixedAssetDepreciationItemDto(x.Id, x.AssetNumber, x.Name, x.Version, c.Amount, c.DepreciableBasis, c.RemainingDepreciableAmount, c.EligibleDays, c.DaysInPeriod, c.Method, c.Explanation, c.Amount > 0m ? "ready" : "skipped"); }).ToArray();
        var hash = Hash(JsonSerializer.Serialize(items.Select(x => new { x.AssetId, x.AssetVersion,
            x.Amount, x.Method, x.Explanation }).ToArray()));
        return new(query.PeriodStart, query.PeriodEnd, Money(items.Sum(x => x.Amount)), hash, items);
    }

    public async Task<FixedAssetDepreciationRunDto> RunDepreciationAsync(RunFixedAssetDepreciationCommand command, CancellationToken ct)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var existing = await _db.FixedAssetDepreciationRuns.Include(x => x.Items).ThenInclude(x => x.Asset)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == command.IdempotencyKey, ct);
        if (existing is not null) return MapRun(existing);
        await ValidatePeriodRangeAsync(command.CompanyId, command.FiscalPeriodId,
            command.PeriodStart, command.PeriodEnd, ct);
        var preview = await PreviewDepreciationAsync(new(command.CompanyId, command.PeriodStart, command.PeriodEnd), ct);
        var now = Now(); var run = new FixedAssetDepreciationRun(Guid.NewGuid(), command.CompanyId,
            command.FiscalPeriodId, command.PeriodStart, command.PeriodEnd, command.IdempotencyKey,
            preview.PopulationHash, command.ActorUserId, now); _db.FixedAssetDepreciationRuns.Add(run);
        await SaveAsync(ct);
        var postedCount = 0; var failures = 0; decimal total = 0m;
        foreach (var candidate in preview.Items)
        {
            var asset = await AssetAsync(command.CompanyId, candidate.AssetId, ct);
            var row = new FixedAssetDepreciationRunItem(Guid.NewGuid(), command.CompanyId, run.Id, asset.Id,
                asset.Version, asset.AssetClassHash, asset.GrossBookValue, asset.AccumulatedDepreciation,
                asset.AccumulatedImpairment, asset.ResidualValue, candidate.Amount, candidate.Explanation);
            _db.FixedAssetDepreciationRunItems.Add(row); await SaveAsync(ct);
            if (candidate.Amount <= 0m) continue;
            try
            {
                await using var transaction = await BeginAtomicWriteAsync(ct);
                var key = $"{command.IdempotencyKey}:{asset.Id:N}";
                var posted = await PostAsync(asset, command.FiscalPeriodId, command.PeriodEnd,
                    asset.AssetClass.VoucherSeriesCode, "fixed_asset_depreciation", "Post fixed-asset depreciation",
                    $"{asset.AssetClassHash}:{asset.Version}:{command.PeriodEnd:yyyyMMdd}", key,
                    command.ActorUserId, command.CorrelationId,
                    [Line(asset.AssetClass.DepreciationExpenseAccountId, candidate.Amount, 0m, asset, "Depreciation expense"),
                     Line(asset.AssetClass.AccumulatedDepreciationAccountId, 0m, candidate.Amount, asset, "Accumulated depreciation")], ct);
                if (!posted.Replay)
                {
                    var calculation = FixedAssetDepreciationCalculator.Calculate(asset,
                        command.PeriodStart, command.PeriodEnd);
                    if (calculation.Amount != candidate.Amount)
                        throw Error(FixedAssetReasonCodes.IdempotencyConflict,
                            "The retained component or asset terms changed after preview.", true);
                    var itemNow = Now(); asset.ApplyDepreciation(candidate.Amount, command.PeriodEnd,
                        calculation.ComponentAllocations, itemNow);
                    AddEvent(asset, FixedAssetEventTypes.Depreciation, command.PeriodEnd, candidate.Amount, 0m,
                        candidate.Amount, 0m, 0m, 0m, $"{asset.AssetClassHash}:{candidate.AssetVersion}", key,
                        command.FiscalPeriodId, posted.JournalId, run.Id, null, command.ActorUserId, itemNow,
                        calculation.ComponentAllocations);
                    row.MarkPosted(posted.JournalId); postedCount++; total += candidate.Amount; _telemetry.DepreciationPosted(candidate.Amount); await SaveAsync(ct);
                }
                else
                {
                    var retainedEvent = await EventAsync(command.CompanyId, key, ct);
                    if (retainedEvent?.LedgerEntryId != posted.JournalId)
                        throw Error(FixedAssetReasonCodes.IdempotencyConflict,
                            "A depreciation journal exists without the retained fixed-asset event.", true);
                    row.MarkPosted(posted.JournalId); postedCount++; total += candidate.Amount;
                    await SaveAsync(ct);
                }
                if (transaction is not null) await transaction.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is FixedAssetException or AccountingPostingException or DbUpdateException)
            { var code = ex is AccountingPostingException p ? p.ReasonCode : FixedAssetReasonCodes.InvalidState; row.MarkFailed(code, Safe(ex.Message)); failures++; _telemetry.Exception(code); await SaveAsync(ct); }
        }
        run.Complete(total, postedCount, failures, Now());
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.FixedAssetDepreciationRunPosted,
            AuditTargetTypes.FixedAssetDepreciationRun, run.Id, $"Depreciation run posted {postedCount} asset items with {failures} exceptions.", command.CorrelationId, Now(), ct);
        await SaveAsync(ct); return await LoadRunAsync(command.CompanyId, run.Id, ct);
    }

    public async Task<FixedAssetReconciliationDto> ReconcileAsync(Guid companyId, CancellationToken ct)
    {
        Required(companyId, nameof(companyId));
        var assets = await _db.FixedAssetRegisterItems.AsNoTracking().Where(x => x.CompanyId == companyId).ToListAsync(ct);
        var classes = await _db.FixedAssetClasses.AsNoTracking().Where(x => x.CompanyId == companyId).ToListAsync(ct);
        var eventLedgerIds = await _db.FixedAssetBookEvents.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == "posted" && x.LedgerEntryId != null).Select(x => x.LedgerEntryId!.Value).ToListAsync(ct);
        var lines = await _db.LedgerEntryLines.AsNoTracking().Where(x => x.CompanyId == companyId && eventLedgerIds.Contains(x.LedgerEntryId)).ToListAsync(ct);
        var costIds = classes.Select(x => x.CostAccountId).ToHashSet(); var depIds = classes.Select(x => x.AccumulatedDepreciationAccountId).ToHashSet(); var impIds = classes.Select(x => x.AccumulatedImpairmentAccountId).ToHashSet();
        var registerCost = Money(assets.Where(x => x.Status != FixedAssetStatuses.Disposed).Sum(x => x.GrossBookValue));
        var registerDep = Money(assets.Where(x => x.Status != FixedAssetStatuses.Disposed).Sum(x => x.AccumulatedDepreciation));
        var registerImp = Money(assets.Where(x => x.Status != FixedAssetStatuses.Disposed).Sum(x => x.AccumulatedImpairment));
        var ledgerCost = Money(lines.Where(x => costIds.Contains(x.FinanceAccountId)).Sum(x => x.DebitAmount - x.CreditAmount));
        var ledgerDep = Money(lines.Where(x => depIds.Contains(x.FinanceAccountId)).Sum(x => x.CreditAmount - x.DebitAmount));
        var ledgerImp = Money(lines.Where(x => impIds.Contains(x.FinanceAccountId)).Sum(x => x.CreditAmount - x.DebitAmount));
        var conflicts = await _db.FixedAssetMigrationConflicts.CountAsync(x => x.CompanyId == companyId && x.Status == "open", ct);
        var issues = new List<string>(); if (registerCost != ledgerCost) issues.Add("Fixed-asset cost differs from linked control-account postings."); if (registerDep != ledgerDep) issues.Add("Accumulated depreciation differs from linked control-account postings."); if (registerImp != ledgerImp) issues.Add("Accumulated impairment differs from linked control-account postings."); if (conflicts > 0) issues.Add("Legacy asset records require explicit migration review.");
        return new(registerCost, ledgerCost, Money(registerCost - ledgerCost), registerDep, ledgerDep,
            Money(registerDep - ledgerDep), registerImp, ledgerImp, Money(registerImp - ledgerImp),
            Money(registerCost - registerDep - registerImp), issues.Count == 0, issues, conflicts);
    }

    public async Task<int> DiscoverLegacyConflictsAsync(Guid companyId, CancellationToken ct)
    {
        Required(companyId, nameof(companyId)); var existing = await _db.FixedAssetMigrationConflicts.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId).Select(x => x.LegacyFinanceAssetId).ToListAsync(ct);
        var legacy = await _db.FinanceAssets.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && !existing.Contains(x.Id))
            .Select(x => new { x.Id, x.ReferenceNumber, x.Name, x.Category, x.PurchasedUtc, x.Amount, x.Currency, x.Status, x.SourceSimulationEventRecordId }).ToListAsync(ct);
        var now = Now(); foreach (var item in legacy)
        {
            _db.FixedAssetMigrationConflicts.Add(new(Guid.NewGuid(), companyId, item.Id,
                "legacy_asset_depreciation_facts_missing", "The legacy purchased/funding record lacks reviewed useful life, residual value, book method, control accounts, and placed-in-service evidence.", JsonSerializer.Serialize(item), now));
        }
        if (legacy.Count > 0) { await AuditAsync(companyId, Guid.Empty, AuditEventActions.FixedAssetMigrationConflictDetected, AuditTargetTypes.FixedAsset, companyId, $"Detected {legacy.Count} legacy asset records requiring explicit migration review.", null, now, ct, AuditActorTypes.System); await SaveAsync(ct); }
        return legacy.Count;
    }

    private async Task<FixedAssetDto> PostAmountLifecycleAsync(FixedAssetLifecycleCommand command,
        string eventType, string postingType, string description,
        Func<FixedAssetRegisterItem, FixedAssetLifecycleCommand, IReadOnlyList<ProposedAccountingLine>> lines,
        Action<FixedAssetRegisterItem, DateTime> apply, decimal costMovement, decimal impairmentMovement,
        CancellationToken ct)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        if (command.Amount <= 0m) throw Error(FixedAssetReasonCodes.InvalidAmount, "The amount must be greater than zero.");
        var asset = await AssetAsync(command.CompanyId, command.AssetId, ct);
        if (await EventAsync(command.CompanyId, command.IdempotencyKey, ct) is { } replay)
        {
            EnsureEventReplay(replay, asset.Id, eventType, command.EffectiveDate, command.SourceVersion,
                command.Amount);
            return Map(asset);
        }
        EnsureVersion(asset.Version, command.ExpectedVersion);
        if (eventType == FixedAssetEventTypes.Improvement) asset.EnsureCanImprove(command.Amount);
        else if (eventType == FixedAssetEventTypes.Impairment) asset.EnsureCanImpair(command.Amount);
        await ValidateOffsetAsync(command.CompanyId, command.OffsetAccountId, ct);
        await using var transaction = await BeginAtomicWriteAsync(ct);
        var posted = await PostAsync(asset, command.FiscalPeriodId, command.EffectiveDate,
            asset.AssetClass.VoucherSeriesCode, postingType, description, command.SourceVersion,
            command.IdempotencyKey, command.ActorUserId, command.CorrelationId, lines(asset, command), ct);
        if (!posted.Replay)
        {
            var now = Now(); apply(asset, now);
            AddEvent(asset, eventType, command.EffectiveDate, command.Amount, costMovement,
                eventType == FixedAssetEventTypes.Depreciation ? command.Amount : 0m, impairmentMovement,
                0m, 0m, command.SourceVersion, command.IdempotencyKey, command.FiscalPeriodId,
                posted.JournalId, null, null, command.ActorUserId, now);
            await LifecycleAuditAsync(command.CompanyId, command.ActorUserId, asset.Id, description, command.CorrelationId, now, ct); await SaveAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await GetAsync(new(command.CompanyId, asset.Id), ct);
    }

    private async Task<(Guid JournalId, bool Replay)> PostAsync(FixedAssetRegisterItem asset,
        Guid fiscalPeriodId, DateOnly date, string series, string postingType, string description,
        string sourceVersion, string idempotencyKey, Guid actor, string? correlation,
        IReadOnlyList<ProposedAccountingLine> lines, CancellationToken ct)
    {
        ValidateIdentity(asset.CompanyId, actor, idempotencyKey); await ValidatePeriodAsync(asset.CompanyId, fiscalPeriodId, date, ct);
        var sourceId = $"{asset.Id:N}:{Hash(idempotencyKey)[..24]}";
        var policy = new Dictionary<string, string> { ["assetId"] = asset.Id.ToString("D"), ["assetClassVersion"] = asset.AssetClassVersion.ToString(), ["assetClassHash"] = asset.AssetClassHash, ["bookMethod"] = asset.BookMethod, ["classRequiresApproval"] = asset.AssetClass.RequiresApproval.ToString().ToLowerInvariant(), ["taxRegisterSupported"] = "false" };
        var result = await _posting.PostAsync(new(new(asset.CompanyId, fiscalPeriodId, series, date, date,
            postingType, description, SourceType, sourceId, sourceVersion, $"fixed-asset:{idempotencyKey}",
            lines, actor, RequiresApproval: false, PolicyFacts: policy), correlation), ct);
        if (result.IsIdempotentReplay && await EventAsync(asset.CompanyId, idempotencyKey, ct) is null)
            throw Error(FixedAssetReasonCodes.IdempotencyConflict,
                "A journal replay exists without the retained fixed-asset event.", true);
        return (result.Journal.Id, result.IsIdempotentReplay);
    }

    private static ProposedAccountingLine Line(Guid accountId, decimal debit, decimal credit,
        FixedAssetRegisterItem asset, string description) => new(accountId, Money(debit), Money(credit),
        asset.Currency, description, DimensionFacts: ParseDimensions(asset.DimensionSnapshotJson));
    private static IReadOnlyDictionary<string, string> ParseDimensions(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    private static IReadOnlyList<FixedAssetComponentDepreciationAllocation> ParseComponentAllocations(
        string json) => JsonSerializer.Deserialize<FixedAssetComponentDepreciationAllocation[]>(json) ?? [];
    private static string Dimensions(IReadOnlyDictionary<string, string>? values) => JsonSerializer.Serialize(
        (values ?? new Dictionary<string, string>()).OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.Ordinal));

    private static FixedAssetRegisterItem BuildTransientRegistration(Guid companyId,
        RegisterFixedAssetInput input, FixedAssetClass assetClass, decimal residual, int usefulLife,
        Guid actorUserId)
    {
        var item = new FixedAssetRegisterItem(Guid.NewGuid(), companyId, assetClass.Id,
            assetClass.Version, assetClass.DefinitionHash, input.AssetNumber, input.Name, input.Currency,
            input.AcquisitionCost, residual, usefulLife, assetClass.BookMethod, input.AcquisitionDate,
            input.SourceType, input.SourceId, input.SourceVersion, input.SourceDocumentId,
            input.LegacyFinanceAssetId, input.Custodian, input.Location, Dimensions(input.DimensionFacts),
            actorUserId, DateTime.UtcNow);
        var components = input.Components ?? [];
        if (components.Count > 25)
            throw Error(FixedAssetReasonCodes.InvalidAmount, "An asset can retain at most 25 book components.");
        if (components.Select(x => x.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != components.Count)
            throw Error(FixedAssetReasonCodes.InvalidAmount, "Component codes must be unique within an asset.");
        if (Money(components.Sum(x => x.Cost)) > item.AcquisitionCost ||
            Money(components.Sum(x => x.ResidualValue)) > item.ResidualValue)
            throw Error(FixedAssetReasonCodes.InvalidAmount, "Component cost and residual totals must fit within the retained asset totals.");
        foreach (var component in components)
        {
            if (component.PlacedInServiceDate < item.AcquisitionDate)
                throw Error(FixedAssetReasonCodes.InvalidState, "A component cannot be placed in service before acquisition.");
            item.Components.Add(new(Guid.NewGuid(), companyId, item.Id, component.Code, component.Name,
                component.Cost, component.ResidualValue, component.UsefulLifeMonths,
                component.PlacedInServiceDate));
        }
        return item;
    }

    private void AddEvent(FixedAssetRegisterItem asset, string eventType, DateOnly date, decimal amount,
        decimal cost, decimal depreciation, decimal impairment, decimal proceeds, decimal gainLoss,
        string sourceVersion, string idempotencyKey, Guid? periodId, Guid? ledgerId, Guid? runId,
        Guid? originalId, Guid actor, DateTime now,
        IReadOnlyList<FixedAssetComponentDepreciationAllocation>? componentAllocations = null)
    {
        var snapshot = Hash(JsonSerializer.Serialize(new { asset.Id, asset.Version, asset.AssetClassVersion,
            asset.AssetClassHash, asset.GrossBookValue, asset.AccumulatedDepreciation,
            asset.AccumulatedImpairment, asset.NetBookValue, eventType, date, amount }));
        _db.FixedAssetBookEvents.Add(new(Guid.NewGuid(), asset.CompanyId, asset.Id, eventType, date,
            amount, cost, depreciation, impairment, proceeds, gainLoss, SourceType, asset.Id.ToString("D"),
            sourceVersion, idempotencyKey, snapshot, periodId, ledgerId, runId, originalId,
            JsonSerializer.Serialize(componentAllocations ?? []), actor, now));
        _telemetry.BookEvent(eventType);
    }

    private async Task ValidateClassAccountsAsync(Guid companyId, FixedAssetClassInput input, CancellationToken ct)
    {
        var ids = new[] { input.CostAccountId, input.AccumulatedDepreciationAccountId, input.DepreciationExpenseAccountId,
            input.AccumulatedImpairmentAccountId, input.ImpairmentExpenseAccountId, input.DisposalGainAccountId,
            input.DisposalLossAccountId };
        if (ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw Error(FixedAssetReasonCodes.CrossCompanyReference, "Every fixed-asset control account must be selected and have a distinct accounting role.");
        var count = await _db.FinanceAccounts.CountAsync(x => x.CompanyId == companyId && ids.Contains(x.Id) && x.IsPostingEnabled, ct);
        if (count != ids.Length) throw Error(FixedAssetReasonCodes.CrossCompanyReference, "One or more fixed-asset accounts are unavailable for this company.");
    }
    private async Task ValidateOffsetAsync(Guid companyId, Guid accountId, CancellationToken ct)
    { if (!await _db.FinanceAccounts.AnyAsync(x => x.CompanyId == companyId && x.Id == Required(accountId, nameof(accountId)) && x.IsPostingEnabled, ct)) throw Error(FixedAssetReasonCodes.CrossCompanyReference, "The selected offset account is unavailable for this company."); }
    private async Task ValidatePeriodAsync(Guid companyId, Guid periodId, DateOnly date, CancellationToken ct)
    {
        var instant = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        if (!await _db.FiscalPeriods.AnyAsync(x => x.CompanyId == companyId && x.Id == Required(periodId, nameof(periodId)) && x.StartUtc <= instant && x.EndUtc > instant, ct))
            throw Error(FixedAssetReasonCodes.PeriodUnavailable, "The selected fiscal period does not contain the posting date.");
    }
    private async Task ValidatePeriodRangeAsync(Guid companyId, Guid periodId, DateOnly start,
        DateOnly end, CancellationToken ct)
    {
        if (end < start)
            throw Error(FixedAssetReasonCodes.PeriodUnavailable,
                "The depreciation period end cannot precede its start.");
        var startInstant = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endInstant = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        if (!await _db.FiscalPeriods.AnyAsync(x => x.CompanyId == companyId &&
                x.Id == Required(periodId, nameof(periodId)) && x.StartUtc <= startInstant &&
                x.EndUtc > endInstant, ct))
            throw Error(FixedAssetReasonCodes.PeriodUnavailable,
                "The selected fiscal period does not contain the complete depreciation range.");
    }
    private async Task ValidateSourceAsync(Guid companyId, Guid? documentId, Guid? legacyId, CancellationToken ct)
    {
        if (documentId.HasValue && !await _db.CompanyKnowledgeDocuments.AnyAsync(x => x.CompanyId == companyId && x.Id == documentId, ct)) throw Error(FixedAssetReasonCodes.CrossCompanyReference, "The source document is unavailable for this company.");
        if (legacyId.HasValue && !await _db.FinanceAssets.AnyAsync(x => x.CompanyId == companyId && x.Id == legacyId, ct)) throw Error(FixedAssetReasonCodes.CrossCompanyReference, "The legacy asset source is unavailable for this company.");
    }
    private async Task<FixedAssetClass> ClassAsync(Guid companyId, Guid id, CancellationToken ct) =>
        await _db.FixedAssetClasses.SingleOrDefaultAsync(x => x.CompanyId == Required(companyId, nameof(companyId)) && x.Id == Required(id, nameof(id)), ct)
        ?? throw Error(FixedAssetReasonCodes.ClassNotFound, "The fixed-asset class was not found.");
    private async Task<FixedAssetRegisterItem> AssetAsync(Guid companyId, Guid id, CancellationToken ct) =>
        await _db.FixedAssetRegisterItems.Include(x => x.AssetClass).Include(x => x.Events)
            .Include(x => x.Components)
            .SingleOrDefaultAsync(x => x.CompanyId == Required(companyId, nameof(companyId)) && x.Id == Required(id, nameof(id)), ct)
        ?? throw Error(FixedAssetReasonCodes.NotFound, "The fixed asset was not found.");
    private Task<FixedAssetBookEvent?> EventAsync(Guid companyId, string key, CancellationToken ct) =>
        _db.FixedAssetBookEvents.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == key, ct);

    private async Task<FixedAssetDepreciationRunDto> LoadRunAsync(Guid companyId, Guid id, CancellationToken ct) =>
        MapRun(await _db.FixedAssetDepreciationRuns.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Asset)
            .SingleAsync(x => x.CompanyId == companyId && x.Id == id, ct));
    private static FixedAssetDepreciationRunDto MapRun(FixedAssetDepreciationRun run) => new(run.Id,
        run.FiscalPeriodId, run.PeriodStart, run.PeriodEnd, run.Status, run.TotalAmount,
        run.PostedItemCount, run.ExceptionCount, run.PopulationHash, run.Version,
        run.Items.Select(x => new FixedAssetDepreciationItemDto(x.AssetId, x.Asset?.AssetNumber ?? string.Empty,
            x.Asset?.Name ?? string.Empty, x.AssetVersion, x.Amount, Math.Max(0m, x.OpeningCost - x.ResidualValue),
            Math.Max(0m, x.OpeningCost - x.ResidualValue - x.OpeningAccumulatedDepreciation - x.OpeningAccumulatedImpairment),
            0, 0, FixedAssetBookMethods.StraightLine, x.CalculationExplanation, x.Status,
            x.LedgerEntryId, x.FailureCode, x.FailureSummary)).ToArray());
    private static FixedAssetClassDto MapClass(FixedAssetClass x) => new(x.Id, x.Code, x.Name,
        x.BookMethod, x.UsefulLifeMonths, x.DefaultResidualPercent, x.CostAccountId,
        x.AccumulatedDepreciationAccountId, x.DepreciationExpenseAccountId, x.AccumulatedImpairmentAccountId,
        x.ImpairmentExpenseAccountId, x.DisposalGainAccountId, x.DisposalLossAccountId,
        x.VoucherSeriesCode, x.RequiresApproval, x.IsActive, x.DefinitionHash, x.Version);
    private static FixedAssetDto Map(FixedAssetRegisterItem x) => new(x.Id, x.AssetClassId,
        x.AssetClass.Code, x.AssetClass.Name, x.AssetNumber, x.Name, x.Currency, x.AcquisitionCost,
        x.ImprovementCost, x.GrossBookValue, x.ResidualValue, x.AccumulatedDepreciation,
        x.AccumulatedImpairment, x.NetBookValue, x.DisposalProceeds, x.DisposalGainLoss,
        x.UsefulLifeMonths, x.BookMethod, x.AcquisitionDate, x.CapitalizationDate, x.PlacedInServiceDate,
        x.LastDepreciationThrough, x.DisposalDate, x.Status, x.SourceType, x.SourceId, x.SourceVersion,
        x.SourceDocumentId, x.LegacyFinanceAssetId, x.Custodian, x.Location,
        ParseDimensions(x.DimensionSnapshotJson), x.Version,
        x.Components.OrderBy(c => c.Code).Select(c => new FixedAssetComponentDto(c.Id, c.Code,
            c.Name, c.Cost, c.ResidualValue, c.AccumulatedDepreciation, c.UsefulLifeMonths,
            c.PlacedInServiceDate)).ToArray(), x.Events.OrderByDescending(e => e.CreatedUtc)
            .Select(e => new FixedAssetEventDto(e.Id, e.EventType, e.EffectiveDate, e.Amount,
                e.CostMovement, e.DepreciationMovement, e.ImpairmentMovement, e.Proceeds, e.GainLoss,
                e.Status, e.LedgerEntryId, e.DepreciationRunId, e.OriginalEventId, e.SourceType,
                e.SourceId, e.SourceVersion, ParseComponentAllocations(e.ComponentAllocationJson)
                    .Select(a => new FixedAssetComponentAllocationDto(a.ComponentId, a.Amount,
                        a.DepreciableBasis, a.RemainingDepreciableAmount, a.EligibleDays,
                        a.DaysInPeriod, a.Explanation)).ToArray(), e.CreatedUtc)).ToArray());

    private async Task LifecycleAuditAsync(Guid companyId, Guid actor, Guid assetId, string summary,
        string? correlation, DateTime now, CancellationToken ct) => await AuditAsync(companyId, actor,
        AuditEventActions.FixedAssetLifecyclePosted, AuditTargetTypes.FixedAsset, assetId, summary, correlation, now, ct);
    private async Task AuditAsync(Guid companyId, Guid actor, string action, string targetType, Guid targetId,
        string summary, string? correlation, DateTime now, CancellationToken ct, string actorType = AuditActorTypes.User) =>
        await _audit.WriteAsync(new(companyId, actorType, actor == Guid.Empty ? null : actor, action,
            targetType, targetId.ToString("D"), AuditEventOutcomes.Succeeded, summary,
            ["fixed_asset_subledger"], new Dictionary<string, string?> { ["taxRegisterSupported"] = "false" },
            correlation, now), ct);
    private async Task SaveAsync(CancellationToken ct) { try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException ex) { throw Error(FixedAssetReasonCodes.IdempotencyConflict, "The fixed-asset record changed. Reload it and retry.", true, ex); } catch (DbUpdateException ex) { throw Error(FixedAssetReasonCodes.SourceConflict, "The fixed-asset identity or source version already exists.", true, ex); } }
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAtomicWriteAsync(
        CancellationToken ct) => _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
        ? await _db.Database.BeginTransactionAsync(ct) : null;
    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static void ValidateIdentity(Guid companyId, Guid actorId, string key) { Required(companyId, nameof(companyId)); Required(actorId, nameof(actorId)); if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 200) throw new ArgumentException("A bounded idempotency key is required.", nameof(key)); }
    private static void EnsureVersion(long actual, long expected) { if (actual != expected) throw Error(FixedAssetReasonCodes.IdempotencyConflict, "The fixed-asset version changed. Reload it and retry.", true); }
    private static void EnsureEventReplay(FixedAssetBookEvent retained, Guid assetId, string eventType,
        DateOnly effectiveDate, string sourceVersion, decimal? amount = null, decimal? proceeds = null,
        Guid? originalEventId = null)
    {
        var matches = retained.AssetId == assetId &&
            string.Equals(retained.EventType, eventType, StringComparison.Ordinal) &&
            retained.EffectiveDate == effectiveDate &&
            string.Equals(retained.SourceVersion, sourceVersion, StringComparison.Ordinal) &&
            (!amount.HasValue || retained.Amount == Money(amount.Value)) &&
            (!proceeds.HasValue || retained.Proceeds == Money(proceeds.Value)) &&
            (!originalEventId.HasValue || retained.OriginalEventId == originalEventId);
        if (!matches)
            throw Error(FixedAssetReasonCodes.IdempotencyConflict,
                "The idempotency key is already bound to different fixed-asset facts.", true);
    }
    private static FixedAssetException Error(string code, string message, bool conflict = false, Exception? inner = null) => inner is null ? new(code, message, conflict) : new FixedAssetException(code, $"{message} {Safe(inner.Message)}", conflict);
    private static string Safe(string value) => value.Length <= 1000 ? value : value[..1000];
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
