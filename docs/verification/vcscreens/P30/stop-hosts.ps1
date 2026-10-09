$ErrorActionPreference='Stop'
$p30Hosts=Get-Content (Join-Path $PSScriptRoot 'owned-hosts.json') -Raw|ConvertFrom-Json
$p30Cleanup=@()
foreach($p30Name in 'api','web') {
    $p30Host=$p30Hosts.$p30Name
    $p30Process=Get-CimInstance Win32_Process -Filter "ProcessId=$($p30Host.pid)"
    if($p30Process) {
        if($p30Process.Name -ne 'dotnet.exe' -or !$p30Process.CommandLine.Contains($p30Host.dll)) {throw "PID ownership changed for $p30Name; refusing to stop."}
        $p30Listener=Get-NetTCPConnection -LocalPort $p30Host.port -State Listen -ErrorAction SilentlyContinue
        if($p30Listener -and @($p30Listener.OwningProcess | Select-Object -Unique) -ne $p30Host.pid) {throw "Listener ownership changed for $p30Name; refusing to stop."}
        Stop-Process -Id $p30Host.pid
        $p30Cleanup+=@{host=$p30Name;pid=$p30Host.pid;dll=$p30Host.dll;stoppedUtc=[DateTime]::UtcNow.ToString('o');ownership='verified exact DLL and listener'}
    } else { $p30Cleanup+=@{host=$p30Name;pid=$p30Host.pid;state='already stopped'} }
}
$p30Cleanup|ConvertTo-Json -Depth 4|Set-Content (Join-Path $PSScriptRoot 'owned-cleanup-final.json')
$p30Cleanup|ConvertTo-Json -Depth 4


