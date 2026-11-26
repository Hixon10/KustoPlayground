#Requires -Version 7.4
param([Parameter(Mandatory)][string]$Binary)

$ErrorActionPreference = 'Stop'
$binaryPath = (Resolve-Path -LiteralPath $Binary).Path
$work = [IO.Directory]::CreateTempSubdirectory('kustoplayground-mcp-')
$lookup = Join-Path $work.FullName 'city lookup.json'
$bonus = Join-Path $work.FullName 'bonus points.csv'
$units = Join-Path $work.FullName 'live units.csv'
$process = [Diagnostics.Process]::new()
$started = $false
$timeout = [TimeSpan]::FromSeconds(30)

function Send-Frame([hashtable]$Frame) {
    $process.StandardInput.WriteLine(($Frame | ConvertTo-Json -Depth 10 -Compress))
    $process.StandardInput.Flush()
}

function Invoke-Rpc([int]$Id, [string]$Method, [hashtable]$Parameters) {
    Send-Frame @{ jsonrpc = '2.0'; id = $Id; method = $Method; params = $Parameters }
    $line = $process.StandardOutput.ReadLineAsync().WaitAsync($timeout).GetAwaiter().GetResult()
    if ($null -eq $line) { throw "MCP server closed stdout before replying to $Method." }
    $reply = $line | ConvertFrom-Json -AsHashtable
    if ($reply.jsonrpc -cne '2.0' -or $reply.id -ne $Id -or !$reply.Contains('result')) {
        throw "Unexpected MCP reply to ${Method}: $line"
    }
    return $reply.result
}

try {
    [IO.File]::WriteAllText($lookup, '{"Name":"Places","Columns":[{"Name":"City","Type":"string"},{"Name":"Region","Type":"string"}],"Rows":[{"City":"Lima","Region":"Peru"}]}')
    [IO.File]::WriteAllText($bonus, "City,Bonus`nLima,3")
    $info = $process.StartInfo
    $info.FileName = $binaryPath
    $info.WorkingDirectory = $work.FullName
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $info.RedirectStandardOutput = $info.RedirectStandardError = $true
    $info.StandardInputEncoding = $info.StandardOutputEncoding = $info.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($arg in @('--mcp-server', '--csv', "City,Score`nOslo,3`nLima,9", '--json-path', 'city lookup.json', '--csv-path', 'bonus points.csv')) {
        $info.ArgumentList.Add($arg)
    }
    $started = $process.Start()
    if (!$started) { throw 'Could not start the native MCP server.' }
    $logs = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.NewLine = "`n"

    $hello = Invoke-Rpc 1 'initialize' @{ protocolVersion = '2025-11-25'; capabilities = @{}; clientInfo = @{ name = 'native-smoke'; version = '1.0' } }
    if ($hello.protocolVersion -cne '2025-11-25' -or !$hello.capabilities.Contains('tools')) { throw 'MCP tool negotiation failed.' }
    Send-Frame @{ jsonrpc = '2.0'; method = 'notifications/initialized' }
    $list = Invoke-Rpc 2 'tools/list' @{}
    if ($list.tools.Count -ne 3 -or $list.tools.name -cnotcontains 'query' -or
        $list.tools.name -cnotcontains 'add_table_from_json' -or $list.tools.name -cnotcontains 'add_table_from_csv') {
        throw 'Expected the query, add_table_from_json, and add_table_from_csv tools.'
    }

    # Later requests must use the loaded database even after its source files disappear.
    [IO.File]::Delete($lookup)
    [IO.File]::Delete($bonus)
    $kql = 'Input | join kind=inner (Places) on City | join kind=inner (Input2) on City | project City, Region, Total=Score+Bonus'
    $joined = Invoke-Rpc 3 'tools/call' @{ name = 'query'; arguments = @{ query = $kql } }
    $items = $joined.structuredContent.results.items
    if ($joined.isError -or $items.Count -ne 2 -or ($items[1] -join ',') -cne 'Lima,Peru,12') { throw 'MCP join of three preloaded tables failed.' }
    $bad = Invoke-Rpc 4 'tools/call' @{ name = 'query'; arguments = @{ query = 'MissingTable | count' } }
    if (!$bad.isError -or $bad.structuredContent.message -notmatch 'MissingTable') { throw 'Expected a recoverable KQL tool error.' }
    $again = Invoke-Rpc 5 'tools/call' @{ name = 'query'; arguments = @{ query = 'Places | count' } }
    $payload = $again.content[0].text | ConvertFrom-Json -AsHashtable
    if ($again.isError -or $payload.status -ne 200 -or $payload.results.items[1][0] -ne 1) { throw 'Second query lost the preloaded table.' }

    $prices = @{ Name = 'Prices'; Columns = @(@{ Name = 'City'; Type = 'string' }, @{ Name = 'Price'; Type = 'int' }); Rows = @(@{ City = 'Lima'; Price = 4 }) }
    $added = Invoke-Rpc 6 'tools/call' @{ name = 'add_table_from_json'; arguments = @{ json = $prices } }
    if ($added.isError -or $added.structuredContent.results.tableName -cne 'Prices') { throw 'Live JSON object import failed.' }
    [IO.File]::WriteAllText($units, "City,Units`nLima,2")
    $added = Invoke-Rpc 7 'tools/call' @{ name = 'add_table_from_csv'; arguments = @{ tableName = 'UnitsByCity'; csvPath = 'live units.csv' } }
    if ($added.isError -or $added.structuredContent.results.tableName -cne 'UnitsByCity') { throw 'Live CSV file import failed.' }
    [IO.File]::Delete($units)

    $duplicate = Invoke-Rpc 8 'tools/call' @{ name = 'add_table_from_csv'; arguments = @{ tableName = 'Input'; csv = "City,Score`nLima,0" } }
    if (!$duplicate.isError -or $duplicate.structuredContent.message -notmatch 'Duplicate table name: Input') { throw 'Live import must not replace a startup table.' }
    $kql = 'Input | join kind=inner (Prices) on City | join kind=inner (UnitsByCity) on City | project City, Total=Score+Price*Units'
    $liveJoin = Invoke-Rpc 9 'tools/call' @{ name = 'query'; arguments = @{ query = $kql } }
    $items = $liveJoin.structuredContent.results.items
    if ($liveJoin.isError -or $items.Count -ne 2 -or ($items[1] -join ',') -cne 'Lima,17') { throw 'Join of live and preloaded tables failed.' }

    $kql = ".show database MyDatabase schema | where ColumnName in ('Price', 'Score', 'Units') | project TableName, ColumnName, ColumnType | sort by TableName asc"
    $catalog = Invoke-Rpc 10 'tools/call' @{ name = 'query'; arguments = @{ query = $kql } }
    $items = $catalog.structuredContent.results.items
    if ($catalog.isError -or $items.Count -ne 4 -or ($items[1] -join ',') -cne 'Input,Score,System.Int32' -or
        ($items[2] -join ',') -cne 'Prices,Price,System.Int32' -or ($items[3] -join ',') -cne 'UnitsByCity,Units,System.Int32') {
        throw 'Database schema did not describe the preloaded and live-imported columns.'
    }

    $process.StandardInput.Close()
    $tail = $process.StandardOutput.ReadToEndAsync()
    if (!$process.WaitForExit([int]$timeout.TotalMilliseconds)) { throw 'MCP server did not stop on stdin EOF.' }
    if ($process.ExitCode -ne 0 -or $tail.GetAwaiter().GetResult().Length -ne 0 -or $logs.GetAwaiter().GetResult() -notmatch 'Starting') {
        throw 'Expected a clean MCP shutdown, protocol-only stdout, and startup logging on stderr.'
    }
} finally {
    if ($started -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
    [IO.File]::Delete($lookup)
    [IO.File]::Delete($bonus)
    [IO.File]::Delete($units)
    [IO.Directory]::Delete($work.FullName)
}
