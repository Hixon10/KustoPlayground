#Requires -Version 7.4
param([Parameter(Mandatory)][string]$Binary)

$ErrorActionPreference = 'Stop'
$binaryPath = (Resolve-Path -LiteralPath $Binary).Path
$work = [IO.Directory]::CreateTempSubdirectory('kustoplayground-mcp-')
$lookup = Join-Path $work.FullName 'city lookup.json'
$bonus = Join-Path $work.FullName 'bonus points.csv'
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
    if ($list.tools.Count -ne 1 -or $list.tools[0].name -cne 'query' -or $list.tools[0].inputSchema.required -notcontains 'query') {
        throw 'Expected exactly one tool, query, with a required query argument.'
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
    [IO.Directory]::Delete($work.FullName)
}
