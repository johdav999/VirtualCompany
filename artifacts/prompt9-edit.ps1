function Edit($path, $old, $new) {
 $value = [IO.File]::ReadAllText((Join-Path $PWD $path)); if (!$value.Contains($old)) { throw "Expected text missing in $path" }; [IO.File]::WriteAllText((Join-Path $PWD $path),$value.Replace($old,$new))
}
Edit 'src/VirtualCompany.Domain/Enums/SalesMeetingCaptureEnums.cs' 'HostMediated, TranscriptAdapter, Voice }' 'HostMediated, TranscriptAdapter, Voice, BrowserRoom }'
Edit 'src/VirtualCompany.Domain/Enums/SalesMeetingCaptureEnums.cs' 'SalesMeetingInputSource.Voice => "voice",' "SalesMeetingInputSource.Voice => `"voice`",`n        SalesMeetingInputSource.BrowserRoom => `"browser_room`","
Edit 'src/VirtualCompany.Domain/Enums/SalesMeetingCaptureEnums.cs' '"voice" => SalesMeetingInputSource.Voice,' "`"voice`" => SalesMeetingInputSource.Voice,`n        `"browser_room`" => SalesMeetingInputSource.BrowserRoom,"
Edit 'src/VirtualCompany.Domain/Entities/SalesRoomAgentTranscript.cs' 'bool overlapped, string text, DateTime createdUtc)' 'bool overlapped, string text, DateTime createdUtc, Guid? stableId = null, Guid? transcriptSegmentId = null, long? agentGeneration = null)'
Edit 'src/VirtualCompany.Domain/Entities/SalesRoomAgentTranscript.cs' 'Id = Guid.NewGuid(); CompanyId' 'Id = stableId ?? Guid.NewGuid(); TranscriptSegmentId = transcriptSegmentId; AgentGeneration = agentGeneration; CompanyId'
Edit 'src/VirtualCompany.Domain/Entities/SalesRoomAgentTranscript.cs' 'public Guid Id { get; private set; }' "public Guid Id { get; private set; }`n    public Guid? TranscriptSegmentId { get; private set; }`n    public long? AgentGeneration { get; private set; }"
Edit 'src/VirtualCompany.Persistence/Persistence/Configurations/SalesRoomAgentTranscriptConfiguration.cs' 'b.ToTable("sales_room_agent_transcripts"); b.HasKey(x => x.Id);' @"
b.ToTable("sales_room_agent_transcripts"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CompanyId, x.TranscriptSegmentId }).IsUnique().HasFilter("[TranscriptSegmentId] IS NOT NULL");
        b.HasOne<SalesMeetingTranscriptSegment>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TranscriptSegmentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
"@
