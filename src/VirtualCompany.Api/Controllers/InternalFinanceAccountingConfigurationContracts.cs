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
public static class SaveCompanyStatutoryProfileRequestMapping
{
    public static CompanyStatutoryProfileInput ToInput(this SaveCompanyStatutoryProfileRequest request) => new(request.LegalName, request.SwedishOrganisationNumber, request.VatRegistrationNumber, request.VatRegistrationStatus, request.RegisteredAddress, request.CorrespondenceAddress, request.CountryCode, request.AccountingCurrency, request.FiscalYearBasis, request.BookkeepingMethod, request.OrganisationRegistrationEffectiveFrom, request.VatRegistrationEffectiveFrom, request.VatRegistrationEffectiveTo, request.IsUserAttested, request.VerificationStatus, request.SourceKind, request.SourceReference, request.SourceCapturedUtc ?? DateTime.UtcNow, request.ExternalVerifier, request.ExternallyVerifiedUtc);
}
