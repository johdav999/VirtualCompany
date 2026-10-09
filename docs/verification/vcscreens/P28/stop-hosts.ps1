$ErrorActionPreference='Stop'
$p28Hosts=Get-Content (Join-Path $PSScriptRoot 'owned-hosts.json') -Raw|ConvertFrom-Json
$p28Cleanup=@()
foreach($p28Name in 'api','web') {
    $p28Host=$p28Hosts.$p28Name
    $p28Process=Get-CimInstance Win32_Process -Filter "ProcessId=$($p28Host.pid)"
    if($p28Process) {
        if($p28Process.Name -ne 'dotnet.exe' -or !$p28Process.CommandLine.Contains($p28Host.dll)) {throw "PID ownership changed for $p28Name; refusing to stop."}
        $p28Listener=Get-NetTCPConnection -LocalPort $p28Host.port -State Listen -ErrorAction SilentlyContinue
        if($p28Listener -and @($p28Listener.OwningProcess | Select-Object -Unique) -ne $p28Host.pid) {throw "Listener ownership changed for $p28Name; refusing to stop."}
        Stop-Process -Id $p28Host.pid
        $p28Cleanup+=@{host=$p28Name;pid=$p28Host.pid;dll=$p28Host.dll;stoppedUtc=[DateTime]::UtcNow.ToString('o');ownership='verified exact DLL and listener'}
    } else { $p28Cleanup+=@{host=$p28Name;pid=$p28Host.pid;state='already stopped'} }
}
$p28Cleanup|ConvertTo-Json -Depth 4|Set-Content (Join-Path $PSScriptRoot 'owned-cleanup-final.json')
$p28Cleanup|ConvertTo-Json -Depth 4
