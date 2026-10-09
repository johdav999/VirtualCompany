$ErrorActionPreference='Stop'
$p29Hosts=Get-Content (Join-Path $PSScriptRoot 'owned-hosts.json') -Raw|ConvertFrom-Json
$p29Cleanup=@()
foreach($p29Name in 'api','web') {
    $p29Host=$p29Hosts.$p29Name
    $p29Process=Get-CimInstance Win32_Process -Filter "ProcessId=$($p29Host.pid)"
    if($p29Process) {
        if($p29Process.Name -ne 'dotnet.exe' -or !$p29Process.CommandLine.Contains($p29Host.dll)) {throw "PID ownership changed for $p29Name; refusing to stop."}
        $p29Listener=Get-NetTCPConnection -LocalPort $p29Host.port -State Listen -ErrorAction SilentlyContinue
        if($p29Listener -and @($p29Listener.OwningProcess | Select-Object -Unique) -ne $p29Host.pid) {throw "Listener ownership changed for $p29Name; refusing to stop."}
        Stop-Process -Id $p29Host.pid
        $p29Cleanup+=@{host=$p29Name;pid=$p29Host.pid;dll=$p29Host.dll;stoppedUtc=[DateTime]::UtcNow.ToString('o');ownership='verified exact DLL and listener'}
    } else { $p29Cleanup+=@{host=$p29Name;pid=$p29Host.pid;state='already stopped'} }
}
$p29Cleanup|ConvertTo-Json -Depth 4|Set-Content (Join-Path $PSScriptRoot 'owned-cleanup-final.json')
$p29Cleanup|ConvertTo-Json -Depth 4

