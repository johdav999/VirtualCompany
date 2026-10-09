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



public sealed class ComplianceCorrectionRequest { public string Reason {get;set;}=string.Empty; public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} }



public sealed class ComplianceEvidenceReviewRequest { public bool Accepted {get;set;} public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} }



public sealed class ComplianceAcknowledgementRequest { public string Kind {get;set;}=string.Empty; public string Reference {get;set;}=string.Empty; public string EvidenceHash {get;set;}=string.Empty; public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} }



public sealed class ComplianceEvidenceRequest { public string Reference {get;set;}=string.Empty; public string EvidenceHash {get;set;}=string.Empty; public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} }



public sealed class ComplianceDecisionRequest { public bool Approved {get;set;} public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} public string? Reason {get;set;} }



public sealed class ComplianceTransitionRequest { public string Action {get;set;}=string.Empty; public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} public string? Reason {get;set;} }


public sealed class GenerateComplianceRequest { public Guid OwnerUserId {get;set;} public string IdempotencyKey {get;set;}=string.Empty; }
