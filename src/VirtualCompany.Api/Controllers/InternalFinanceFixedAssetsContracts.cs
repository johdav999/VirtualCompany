using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Api.ProblemHandling;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

public sealed class SaveFixedAssetClassRequest
{
    public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string BookMethod { get; set; } = "straight_line"; public int UsefulLifeMonths { get; set; } = 60;
    public decimal DefaultResidualPercent { get; set; } public Guid CostAccountId { get; set; }
    public Guid AccumulatedDepreciationAccountId { get; set; } public Guid DepreciationExpenseAccountId { get; set; }
    public Guid AccumulatedImpairmentAccountId { get; set; } public Guid ImpairmentExpenseAccountId { get; set; }
    public Guid DisposalGainAccountId { get; set; } public Guid DisposalLossAccountId { get; set; }
    public string VoucherSeriesCode { get; set; } = "A"; public bool RequiresApproval { get; set; } = true;
    public long ExpectedVersion { get; set; }
    public FixedAssetClassInput ToInput() => new(Code, Name, BookMethod, UsefulLifeMonths,
        DefaultResidualPercent, CostAccountId, AccumulatedDepreciationAccountId, DepreciationExpenseAccountId,
        AccumulatedImpairmentAccountId, ImpairmentExpenseAccountId, DisposalGainAccountId,
        DisposalLossAccountId, VoucherSeriesCode, RequiresApproval);
}


public sealed class RegisterFixedAssetRequest
{
    public Guid AssetClassId { get; set; } public string AssetNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; public string Currency { get; set; } = "SEK";
    public decimal AcquisitionCost { get; set; } public decimal? ResidualValue { get; set; }
    public int? UsefulLifeMonths { get; set; } public DateOnly AcquisitionDate { get; set; }
    public string SourceType { get; set; } = string.Empty; public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty; public Guid? SourceDocumentId { get; set; }
    public Guid? LegacyFinanceAssetId { get; set; } public string? Custodian { get; set; } public string? Location { get; set; }
    public Dictionary<string, string> DimensionFacts { get; set; } = []; public string IdempotencyKey { get; set; } = string.Empty;
    public List<FixedAssetComponentRequest> Components { get; set; } = [];
    public RegisterFixedAssetInput ToInput() => new(AssetClassId, AssetNumber, Name, Currency,
        AcquisitionCost, ResidualValue, UsefulLifeMonths, AcquisitionDate, SourceType, SourceId,
        SourceVersion, SourceDocumentId, LegacyFinanceAssetId, Custodian, Location, DimensionFacts,
        Components.Select(x => x.ToInput()).ToArray());
}


public sealed class FixedAssetComponentRequest
{
    public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public decimal Cost { get; set; } public decimal ResidualValue { get; set; }
    public int UsefulLifeMonths { get; set; } public DateOnly PlacedInServiceDate { get; set; }
    public FixedAssetComponentInput ToInput() => new(Code, Name, Cost, ResidualValue,
        UsefulLifeMonths, PlacedInServiceDate);
}


public sealed class FixedAssetLifecycleRequest
{
    public DateOnly EffectiveDate { get; set; } public Guid FiscalPeriodId { get; set; }
    public Guid OffsetAccountId { get; set; } public decimal Amount { get; set; } public long ExpectedVersion { get; set; }
    public string SourceVersion { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty;
    public FixedAssetLifecycleCommand ToCommand(Guid companyId, Guid assetId, Guid actor, string? correlation) =>
        new(companyId, assetId, EffectiveDate, FiscalPeriodId, OffsetAccountId, Amount, ExpectedVersion,
            SourceVersion, IdempotencyKey, actor, correlation);
}


