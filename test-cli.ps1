#Requires -Version 7.4
param([Parameter(Mandatory)][string]$Binary)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$binaryPath = (Resolve-Path -LiteralPath $Binary).Path
$isolated = [IO.Directory]::CreateTempSubdirectory('kustoplayground-cli-')
$exe = Join-Path $isolated.FullName ([IO.Path]::GetFileName($binaryPath))
try {
    # A framework-dependent launcher or executable needing sibling DLLs must not pass.
    Copy-Item -LiteralPath $binaryPath -Destination $exe
    Push-Location $isolated.FullName
    try {
        $help = & $exe --help | Out-String
        if ($help -notmatch '--execute' -or $help -notmatch '--query') { throw 'Incomplete help.' }

        $definition = '{"Name":"Orders","Columns":[{"Name":"Items","Type":"dynamic"}],"Rows":[{"Items":[{"n":2},{"n":5}]},{"Items":[]}]}'
        $query = 'Orders | mv-expand Items | summarize Total=sum(tolong(Items.n))'
        $json = & $exe -e --json $definition --query $query | ConvertFrom-Json -AsHashtable
        if ($json.status -ne 200 -or $json.results.items.Count -ne 2 -or
            $json.results.items[0].Total -cne 'long' -or $json.results.items[1][0] -ne 7) {
            throw 'Imported dynamic JSON query did not return Total:long = 7.'
        }

        $csv = "City,Score`nOslo,3`nLima,9"
        $result = & $exe -execute --query 'Input | where Score > 5 | project City' --csv $csv | ConvertFrom-Json -AsHashtable
        if ($result.status -ne 200 -or $result.results.items.Count -ne 2 -or
            $result.results.items[0].City -cne 'string' -or $result.results.items[1][0] -cne 'Lima') {
            throw 'CSV query did not return City:string = Lima.'
        }
        Write-Output 'Native CLI smoke passed: help, JSON expansion/aggregation, and CSV filtering.'
    } finally { Pop-Location }
} finally {
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe }
    [IO.Directory]::Delete($isolated.FullName)
}
