using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Companies;

internal static class GuidedWorkTelemetry
{
    private static readonly Meter Meter=new("VirtualCompany.GuidedWork","1.0.0");
    internal static readonly Counter<long> SessionsStarted=Meter.CreateCounter<long>("guided_work.sessions.started");
    internal static readonly Counter<long> TurnsCompleted=Meter.CreateCounter<long>("guided_work.turns.completed");
    internal static readonly Counter<long> FieldsChanged=Meter.CreateCounter<long>("guided_work.fields.changed");
    internal static readonly Counter<long> ReviewsPrepared=Meter.CreateCounter<long>("guided_work.reviews.prepared");
    internal static readonly Counter<long> ArtifactsCommitted=Meter.CreateCounter<long>("guided_work.artifacts.committed");
    internal static readonly Counter<long> CheckpointFailures=Meter.CreateCounter<long>("guided_work.checkpoints.failed");
    internal static readonly Counter<long> DeduplicatedOperations=Meter.CreateCounter<long>("guided_work.operations.deduplicated");
    internal static readonly Counter<long> CommitConflicts=Meter.CreateCounter<long>("guided_work.commits.conflicted");
    internal static readonly Counter<long> CommitFailures=Meter.CreateCounter<long>("guided_work.commits.failed");
    internal static readonly Counter<long> VoiceCallsStarted=Meter.CreateCounter<long>("guided_work.voice.calls.started");
    internal static readonly Counter<long> VoiceReconnects=Meter.CreateCounter<long>("guided_work.voice.reconnects");
    internal static readonly Counter<long> VoiceInterruptions=Meter.CreateCounter<long>("guided_work.voice.interruptions");
    internal static readonly Counter<long> VoiceToolRejected=Meter.CreateCounter<long>("guided_work.voice.tools.rejected");
    internal static readonly Counter<long> ResearchCompleted=Meter.CreateCounter<long>("guided_work.research.completed");
    internal static readonly Histogram<double> CheckpointDuration=Meter.CreateHistogram<double>("guided_work.checkpoint.duration","ms");
    internal static readonly Histogram<double> VoiceCallDuration=Meter.CreateHistogram<double>("guided_work.voice.call.duration","s");
    internal static readonly Histogram<double> ResearchDuration=Meter.CreateHistogram<double>("guided_work.research.duration","ms");
}
