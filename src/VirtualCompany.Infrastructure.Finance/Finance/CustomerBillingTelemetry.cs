using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerBillingTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.Finance.CustomerBilling", "1.0.0");
    private static readonly Counter<long> ProfileUpdates = Meter.CreateCounter<long>("customer_billing_profile_updates");
    private static readonly Counter<long> DuplicateCandidates = Meter.CreateCounter<long>("customer_duplicate_candidates");
    private static readonly Counter<long> DuplicateDecisions = Meter.CreateCounter<long>("customer_duplicate_decisions");
    private static readonly Counter<long> SourceConflicts = Meter.CreateCounter<long>("customer_billing_source_conflicts");
    private readonly ILogger<CustomerBillingTelemetry> _logger;

    public CustomerBillingTelemetry(ILogger<CustomerBillingTelemetry> logger) => _logger = logger;

    public void ProfileSaved(Guid companyId, Guid counterpartyId, string sourceKind, long version)
    {
        ProfileUpdates.Add(1, new KeyValuePair<string, object?>("source", sourceKind));
        _logger.LogInformation("Customer billing profile {CounterpartyId} in company {CompanyId} was saved at version {Version} from source {SourceKind}.", counterpartyId, companyId, version, sourceKind);
    }

    public void CandidateDetected(Guid companyId, Guid candidateId, int score)
    {
        DuplicateCandidates.Add(1);
        _logger.LogInformation("Customer duplicate candidate {CandidateId} in company {CompanyId} was detected with deterministic score {Score}.", candidateId, companyId, score);
    }

    public void DecisionRecorded(Guid companyId, Guid candidateId, string decision)
    {
        DuplicateDecisions.Add(1, new KeyValuePair<string, object?>("decision", decision));
        _logger.LogInformation("Customer duplicate candidate {CandidateId} in company {CompanyId} received decision {Decision}.", candidateId, companyId, decision);
    }

    public void ConflictDetected(Guid companyId, Guid counterpartyId, string existingSource, string incomingSource)
    {
        SourceConflicts.Add(1, new("existing_source", existingSource), new("incoming_source", incomingSource));
        _logger.LogWarning("Customer billing profile {CounterpartyId} in company {CompanyId} has a source conflict between {ExistingSource} and {IncomingSource}.", counterpartyId, companyId, existingSource, incomingSource);
    }
}
