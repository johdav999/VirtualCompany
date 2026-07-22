using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class ConversationTaskLinkConfiguration : IEntityTypeConfiguration<ConversationTaskLink>
{
    public void Configure(EntityTypeBuilder<ConversationTaskLink> builder)
    {
        builder.ToTable("conversation_task_links");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(x => x.MessageId).HasColumnName("message_id");
        builder.Property(x => x.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(x => x.LinkType).HasColumnName("link_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ConversationId });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId });
        builder.HasIndex(x => new { x.CompanyId, x.ConversationId, x.TaskId, x.MessageId }).IsUnique();

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Conversation)
            .WithMany(x => x.TaskLinks)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Message)
            .WithMany(x => x.TaskLinks)
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Task)
            .WithMany(x => x.ConversationLinks)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

