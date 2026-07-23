using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AgentOrchestrationRunConfiguration : IEntityTypeConfiguration<AgentOrchestrationRun>
{
    public void Configure(EntityTypeBuilder<AgentOrchestrationRun> b)
    {
        b.ToTable("agent_orchestration_runs"); b.HasKey(x => x.Id);
        b.Property(x => x.CapabilityId).HasMaxLength(100).IsRequired(); b.Property(x => x.CapabilityVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.PromptVersion).HasMaxLength(32).IsRequired(); b.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasMaxLength(32).IsRequired(); b.Property(x => x.Provider).HasMaxLength(100); b.Property(x => x.Model).HasMaxLength(200);
        b.Property(x => x.Confidence).HasColumnType("decimal(5,4)"); b.Property(x => x.Summary).HasMaxLength(2000);
        b.Property(x => x.ResultJson).HasColumnType("nvarchar(max)"); b.Property(x => x.SourceIdsJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.FailureCode).HasMaxLength(100); b.Property(x => x.FailureMessage).HasMaxLength(1000); b.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken().IsRequired(); b.HasIndex(x => new { x.CompanyId, x.AgentId, x.CreatedUtc });
        b.HasIndex(x => new { x.CompanyId, x.CapabilityId, x.Status }); b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AgentHandoffConfiguration : IEntityTypeConfiguration<AgentHandoff>
{
    public void Configure(EntityTypeBuilder<AgentHandoff> b)
    {
        b.ToTable("agent_handoffs"); b.HasKey(x => x.Id); b.Property(x => x.Type).HasMaxLength(100).IsRequired(); b.Property(x => x.Version).HasMaxLength(32).IsRequired();
        b.Property(x => x.Objective).HasMaxLength(1000).IsRequired(); b.Property(x => x.RequestedOutcome).HasMaxLength(1000).IsRequired(); b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.EvidenceJson).HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.CompletionSummary).HasMaxLength(2000); b.Property(x => x.FailureReason).HasMaxLength(1000);
        b.Property(x => x.Confidence).HasColumnType("decimal(5,4)"); b.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.ReceivingAgentId, x.Status, x.DueUtc }); b.HasIndex(x => new { x.CompanyId, x.RequestingAgentId, x.CreatedUtc });
        b.HasIndex(x => new { x.CompanyId, x.CorrelationId }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Agent>().WithMany().HasForeignKey(x => x.RequestingAgentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Agent>().WithMany().HasForeignKey(x => x.ReceivingAgentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AgentMemoryCandidateConfiguration : IEntityTypeConfiguration<AgentMemoryCandidate>
{
    public void Configure(EntityTypeBuilder<AgentMemoryCandidate> b)
    {
        b.ToTable("agent_memory_candidates"); b.HasKey(x => x.Id); b.Property(x => x.MemoryType).HasMaxLength(64).IsRequired(); b.Property(x => x.Scope).HasMaxLength(64).IsRequired();
        b.Property(x => x.Content).HasMaxLength(4000).IsRequired(); b.Property(x => x.EvidenceJson).HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.Confidence).HasColumnType("decimal(5,4)");
        b.Property(x => x.Sensitivity).HasMaxLength(32).IsRequired(); b.Property(x => x.Fingerprint).HasMaxLength(128).IsRequired(); b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.ReviewReason).HasMaxLength(500); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().IsRequired(); b.HasIndex(x => new { x.CompanyId, x.Status, x.ExpiresUtc });
        b.HasIndex(x => new { x.CompanyId, x.Fingerprint }).IsUnique(); b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Agent>().WithMany().HasForeignKey(x => x.ProposingAgentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AgentAiQualityEventConfiguration : IEntityTypeConfiguration<AgentAiQualityEvent>
{
    public void Configure(EntityTypeBuilder<AgentAiQualityEvent> b)
    {
        b.ToTable("agent_ai_quality_events"); b.HasKey(x => x.Id); b.Property(x => x.CapabilityId).HasMaxLength(100).IsRequired(); b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        b.Property(x => x.EventIdentity).HasMaxLength(200).IsRequired(); b.Property(x => x.ReasonCode).HasMaxLength(100); b.Property(x => x.Comment).HasMaxLength(1000);
        b.Property(x => x.Confidence).HasColumnType("decimal(5,4)"); b.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.EventIdentity }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.AgentId, x.CapabilityId, x.OccurredUtc });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade); b.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}
