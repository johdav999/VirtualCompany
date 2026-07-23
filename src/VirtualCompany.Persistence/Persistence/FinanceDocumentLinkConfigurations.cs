using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceTransactionDocumentLinkConfiguration : IEntityTypeConfiguration<FinanceTransaction>
{
    public void Configure(EntityTypeBuilder<FinanceTransaction> builder)
    {
        builder.Property(x => x.DocumentId).HasColumnName("document_id");
        builder.HasIndex(x => new { x.CompanyId, x.DocumentId });
        builder.HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DocumentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FinanceInvoiceDocumentLinkConfiguration : IEntityTypeConfiguration<FinanceInvoice>
{
    public void Configure(EntityTypeBuilder<FinanceInvoice> builder)
    {
        builder.Property(x => x.DocumentId).HasColumnName("document_id");
        builder.HasIndex(x => new { x.CompanyId, x.DocumentId });
        builder.HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DocumentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FinanceBillDocumentLinkConfiguration : IEntityTypeConfiguration<FinanceBill>
{
    public void Configure(EntityTypeBuilder<FinanceBill> builder)
    {
        builder.Property(x => x.DocumentId).HasColumnName("document_id");
        builder.HasIndex(x => new { x.CompanyId, x.DocumentId });
        builder.HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DocumentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompanyKnowledgeDocumentFinanceLinkConfiguration : IEntityTypeConfiguration<CompanyKnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<CompanyKnowledgeDocument> builder)
    {
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
    }
}
