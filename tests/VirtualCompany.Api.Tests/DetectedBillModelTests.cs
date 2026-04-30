using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DetectedBillModelTests
{
    [Fact]
    public void DetectedBillModel_MapsNormalizedFieldsAndWorkflowState()
    {
        using var dbContext = CreateContext();
        var entity = dbContext.Model.FindEntityType(typeof(DetectedBill));

        Assert.NotNull(entity);
        Assert.Equal("detected_bills", entity!.GetTableName());
        Assert.Equal("decimal(18,2)", entity.FindProperty(nameof(DetectedBill.TotalAmount))!.GetColumnType());
        Assert.Equal("decimal(18,2)", entity.FindProperty(nameof(DetectedBill.VatAmount))!.GetColumnType());
        Assert.Equal("decimal(5,4)", entity.FindProperty(nameof(DetectedBill.Confidence))!.GetColumnType());
        Assert.Equal(3, entity.FindProperty(nameof(DetectedBill.Currency))!.GetMaxLength());
        Assert.Equal(34, entity.FindProperty(nameof(DetectedBill.Iban))!.GetMaxLength());
        Assert.Equal(11, entity.FindProperty(nameof(DetectedBill.Bic))!.GetMaxLength());

        Assert.NotNull(entity.FindProperty(nameof(DetectedBill.ValidationStatus)));
        Assert.NotNull(entity.FindProperty(nameof(DetectedBill.ReviewStatus)));
        Assert.NotNull(entity.FindProperty(nameof(DetectedBill.RequiresReview)));
        Assert.NotNull(entity.FindProperty(nameof(DetectedBill.IsEligibleForApprovalProposal)));
        Assert.NotNull(entity.FindProperty(nameof(DetectedBill.ValidationStatusPersisted)));
        Assert.NotNull(entity.FindProperty(nameof(DetectedBill.ValidationIssuesJson)));
        Assert.NotNull(entity.FindProperty(nameof(DetectedBill.DuplicateCheckId)));

        Assert.Contains(entity.GetIndexes(), index =>
            HasProperties(index, nameof(DetectedBill.CompanyId), nameof(DetectedBill.SourceEmailId)));
        Assert.Contains(entity.GetIndexes(), index =>
            HasProperties(index, nameof(DetectedBill.CompanyId), nameof(DetectedBill.SourceAttachmentId)));
        Assert.Contains(entity.GetIndexes(), index =>
            HasProperties(index, nameof(DetectedBill.CompanyId), nameof(DetectedBill.SupplierName), nameof(DetectedBill.InvoiceNumber), nameof(DetectedBill.TotalAmount)));
    }

    [Fact]
    public void DetectedBillFieldModel_MapsEvidenceReferencesAndUniqueFieldRows()
    {
        using var dbContext = CreateContext();
        var entity = dbContext.Model.FindEntityType(typeof(DetectedBillField));

        Assert.NotNull(entity);
        Assert.Equal("detected_bill_fields", entity!.GetTableName());
        Assert.Equal("decimal(5,4)", entity.FindProperty(nameof(DetectedBillField.FieldConfidence))!.GetColumnType());
        Assert.Equal(512, entity.FindProperty(nameof(DetectedBillField.SourceDocument))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(DetectedBillField.PageReference))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(DetectedBillField.SectionReference))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(DetectedBillField.TextSpan))!.GetMaxLength());
        Assert.Equal(512, entity.FindProperty(nameof(DetectedBillField.Locator))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(DetectedBillField.ExtractionMethod))!.GetMaxLength());

        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            HasProperties(index, nameof(DetectedBillField.CompanyId), nameof(DetectedBillField.DetectedBillId), nameof(DetectedBillField.FieldName)));
    }

    [Fact]
    public void BillDuplicateCheckModel_StoresResultStatusForDetectedBillDuplicatePersistence()
    {
        using var dbContext = CreateContext();
        var entity = dbContext.Model.FindEntityType(typeof(BillDuplicateCheck));

        Assert.NotNull(entity);
        Assert.Equal(32, entity!.FindProperty(nameof(BillDuplicateCheck.ResultStatus))!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), index =>
            HasProperties(index, nameof(BillDuplicateCheck.CompanyId), nameof(BillDuplicateCheck.ResultStatus), nameof(BillDuplicateCheck.CheckedUtc)));
    }

    private static VirtualCompanyDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static bool HasProperties(Microsoft.EntityFrameworkCore.Metadata.IReadOnlyIndex index, params string[] propertyNames)
    {
        var actual = index.Properties.Select(property => property.Name).ToArray();
        return actual.SequenceEqual(propertyNames, StringComparer.Ordinal);
    }
}
