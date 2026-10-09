using System.Globalization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;



public sealed class RunFixedAssetDepreciationRequest { public Guid FiscalPeriodId { get; set; } public DateOnly PeriodStart { get; set; } public DateOnly PeriodEnd { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }



public sealed class ReverseFixedAssetEventRequest { public Guid FiscalPeriodId { get; set; } public DateOnly PostingDate { get; set; } public string Reason { get; set; } = string.Empty; public string SourceVersion { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty; public long ExpectedVersion { get; set; } }



public sealed class DisposeFixedAssetRequest { public DateOnly DisposalDate { get; set; } public Guid FiscalPeriodId { get; set; } public Guid ProceedsAccountId { get; set; } public decimal Proceeds { get; set; } public long ExpectedVersion { get; set; } public string SourceVersion { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty; }



public sealed class TransferFixedAssetRequest { public string? Custodian { get; set; } public string? Location { get; set; } public Dictionary<string, string> DimensionFacts { get; set; } = []; public long ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }



public sealed class PlaceFixedAssetInServiceRequest { public DateOnly PlacedInServiceDate { get; set; } public long ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
