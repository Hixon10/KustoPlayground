#Requires -Version 7.4
param([Parameter(Mandatory)][string]$Binary)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$binaryPath = (Resolve-Path -LiteralPath $Binary).Path
$isolated = [IO.Directory]::CreateTempSubdirectory('kustoplayground-cli-')
$exe = Join-Path $isolated.FullName ([IO.Path]::GetFileName($binaryPath))
$jsonFile = Join-Path $isolated.FullName 'order details.json'
$csvFile = Join-Path $isolated.FullName 'city scores.csv'
try {
    # A framework-dependent launcher or executable needing sibling DLLs must not pass.
    Copy-Item -LiteralPath $binaryPath -Destination $exe
    Push-Location $isolated.FullName
    try {
        $help = & $exe --help | Out-String
        if ($help -notmatch '--execute' -or $help -notmatch '--query' -or
            $help -notmatch '--json-file' -or $help -notmatch '--csv-file') { throw 'Incomplete help.' }
        if ((& $exe | Out-String) -cne $help) { throw 'No arguments should print the same help as --help.' }

        $catalog = & $exe --query '.show databases | where IsCurrent | project DatabaseName' | ConvertFrom-Json -AsHashtable
        if ($catalog.status -ne 200 -or $catalog.results.items.Count -ne 2 -or $catalog.results.items[1][0] -cne 'MyDatabase') {
            throw 'The native CLI did not report MyDatabase as its current database.'
        }

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

        [IO.File]::WriteAllText($jsonFile, $definition)
        [IO.File]::WriteAllText($csvFile, $csv)
        $fromJson = & $exe -e --json-file $jsonFile --query $query | ConvertFrom-Json -AsHashtable
        $fromCsv = & $exe -e --csv-file 'city scores.csv' --query 'Input | where Score > 5 | project City' | ConvertFrom-Json -AsHashtable
        if (($fromJson.results | ConvertTo-Json -Depth 10 -Compress) -cne ($json.results | ConvertTo-Json -Depth 10 -Compress) -or
            ($fromCsv.results | ConvertTo-Json -Depth 10 -Compress) -cne ($result.results | ConvertTo-Json -Depth 10 -Compress)) {
            throw 'File input results differed from the equivalent inline queries.'
        }

        $series = & $exe -e --query 'datatable(X:real,V:long)[0.0,2, 1.0,7] | make-series S=sum(V) on X from 0.0 to 1.5 step 0.5' | ConvertFrom-Json -AsHashtable
        if ($series.status -ne 200 -or $series.results.items.Count -ne 2 -or
            ($series.results.items[1][0] -join ',') -cne '2,0,7' -or ($series.results.items[1][1] -join ',') -cne '0,0.5,1') {
            throw 'make-series did not produce the expected real axis and gap-filled sums.'
        }
        $chart = & $exe -e --query 'datatable(X:long,Y:long)[0,2,1,5] | render linechart' | ConvertFrom-Json -AsHashtable
        if ($chart.status -ne 200 -or $chart.results.items.Count -ne 3 -or
            $chart.results.items[0].Y -cne 'long' -or ($chart.results.items[2] -join ',') -cne '1,5') {
            throw 'Terminal render did not preserve the ordinary tabular JSON result.'
        }
        & (Join-Path $PSScriptRoot 'test-cli-mcp.ps1') -Binary $exe
        Write-Output 'Native CLI smoke passed: help, inputs, make-series, render, and multi-table MCP over stdio.'
    } finally { Pop-Location }
} finally {
    foreach ($path in @($exe, $jsonFile, $csvFile)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    [IO.Directory]::Delete($isolated.FullName)
}
