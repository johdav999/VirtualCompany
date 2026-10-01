param([switch]$LiveProviderProbe)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $repo
try {
    # Isolated fixtures only. No database resets, meeting changes, consent or rollout writes.
    dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --filter 'FullyQualifiedName~SalesRoom|FullyQualifiedName~SalesMeetingDialogueEvidence|FullyQualifiedName~OpenAiRealtime|FullyQualifiedName~RealtimeBufferedSpeech|FullyQualifiedName~SalesMeetingCaptureServiceTests|FullyQualifiedName~SalesBrowserRoomAgent' --logger 'trx;LogFileName=agentdialog.trx' -v:minimal -clp:ErrorsOnly
    if ($LASTEXITCODE) { throw 'Backend dialogue regression failed.' }
    dotnet test tests/VirtualCompany.SalesSource.Tests/VirtualCompany.SalesSource.Tests.csproj --no-restore --filter FullyQualifiedName~AgentConversationTests -v:q -clp:ErrorsOnly
    if ($LASTEXITCODE) { throw 'Shared lifecycle regression failed.' }
    dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter FullyQualifiedName~SalesHumanRoomTests -v:q -clp:ErrorsOnly
    if ($LASTEXITCODE) { throw 'Room UI regression failed.' }
    node --test tests/VirtualCompany.Web.Tests/js/sales-human-room.test.mjs
    if ($LASTEXITCODE) { throw 'Browser media regression failed.' }
    if ($LiveProviderProbe) {
        # Billable synthetic text/tool/audio generation only; no microphone or publication.
        dotnet run --project scripts/AgentDialogueProbe/AgentDialogueProbe.csproj --no-restore -- --live --playback
        if ($LASTEXITCODE) { throw 'Provider compatibility probe failed.' }
    }
} finally { Pop-Location }
