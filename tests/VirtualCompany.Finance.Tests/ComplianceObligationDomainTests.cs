using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class ComplianceObligationDomainTests
{
    private static readonly Guid CompanyId=Guid.NewGuid(), Preparer=Guid.NewGuid(), Approver=Guid.NewGuid();
    private static ComplianceObligationInstance Create()=>new(Guid.NewGuid(),CompanyId,"se_vat_return","Swedish VAT return","SE","sweden-statutory-candidate","1.4.0",new string('a',64),"vat_filing_period.explicit_due_date",new(2026,9,14),Preparer,Guid.NewGuid(),Guid.NewGuid(),null,new string('b',64),Preparer,DateTime.UtcNow);

    [Fact] public void Due_date_before_period_end_is_rejected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>new VatFilingPeriod(Guid.NewGuid(),CompanyId,"2026-08",new(2026,8,1),new(2026,8,31),"SEK",null,DateTime.UtcNow,new(2026,8,30)));
    [Fact] public void Preparer_cannot_self_approve(){var x=Create();x.Prepare(Preparer,DateTime.UtcNow);x.SubmitForReview(Preparer,DateTime.UtcNow);Assert.Throws<InvalidOperationException>(()=>x.Decide(Preparer,true,DateTime.UtcNow));}
    [Fact] public void Export_does_not_imply_submission_or_authority_receipt(){var x=Create();x.Prepare(Preparer,DateTime.UtcNow);x.SubmitForReview(Preparer,DateTime.UtcNow);x.Decide(Approver,true,DateTime.UtcNow);x.RecordExport("vat-return:1/package",new string('c',64),DateTime.UtcNow);Assert.Equal(ComplianceObligationStatuses.Exported,x.Status);Assert.Empty(x.SubmissionEvidence);Assert.Empty(x.Acknowledgements);}
    [Fact] public void Authority_approval_requires_a_prior_receipt(){var x=Create();x.Prepare(Preparer,DateTime.UtcNow);x.SubmitForReview(Preparer,DateTime.UtcNow);x.Decide(Approver,true,DateTime.UtcNow);x.RecordExport("vat-return:1/package",new string('c',64),DateTime.UtcNow);x.RecordManualSubmission(DateTime.UtcNow);Assert.Throws<InvalidOperationException>(()=>x.RecordAcknowledgement("approved",DateTime.UtcNow));}
    [Fact] public void Authority_states_are_separate(){var x=Create();x.Prepare(Preparer,DateTime.UtcNow);x.SubmitForReview(Preparer,DateTime.UtcNow);x.Decide(Approver,true,DateTime.UtcNow);x.RecordExport("vat-return:1/package",new string('c',64),DateTime.UtcNow);x.RecordManualSubmission(DateTime.UtcNow);x.RecordAcknowledgement("received",DateTime.UtcNow);Assert.Equal(ComplianceObligationStatuses.AuthorityReceived,x.Status);x.RecordAcknowledgement("approved",DateTime.UtcNow);Assert.Equal(ComplianceObligationStatuses.AuthorityApproved,x.Status);}
    [Fact] public void Submission_evidence_requires_independent_review(){var evidence=new ComplianceSubmissionEvidence(Guid.NewGuid(),CompanyId,Guid.NewGuid(),"receipt.pdf",new string('d',64),Preparer,DateTime.UtcNow);Assert.Throws<InvalidOperationException>(()=>evidence.Review(Preparer,true,DateTime.UtcNow));evidence.Review(Approver,true,DateTime.UtcNow);Assert.Equal("accepted",evidence.ReviewStatus);}
    [Fact] public void Correction_retains_bidirectional_links(){var original=Create();var correction=Create();correction.SetCorrectionOf(original.Id,DateTime.UtcNow);original.LinkCorrection(correction.Id,DateTime.UtcNow);Assert.Equal(original.Id,correction.CorrectionOfInstanceId);Assert.Equal(correction.Id,original.CorrectedByInstanceId);Assert.Equal(ComplianceObligationStatuses.Corrected,original.Status);}
}
