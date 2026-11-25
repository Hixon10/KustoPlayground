# Kusto Playground

**KustoPlayground** is a client-side playground for experimenting with [Kusto Query Language (KQL)](https://learn.microsoft.com/en-us/azure/data-explorer/kusto/query/).  

Run the same in-memory engine in the **Blazor WebAssembly playground** or the
**native command-line client**. Neither client connects to an Azure cluster.

## Features
- Create an **in-memory Kusto database** directly in your browser from JSON, or CSV.
- Write and run **KQL queries** interactively.
- Draw line and time charts with multiple measures and optional grouping using [`render`](#charts).
- 100% client-side - no data leaves your machine.
- Perfect for **learning, prototyping, and testing** queries quickly.

## Native CLI

Download the archive for your platform from the [latest GitHub Release](https://github.com/Hixon10/KustoPlayground/releases/latest):
`kustoplayground-linux-x64.tar.gz`, `kustoplayground-win-x64.zip`, or
`kustoplayground-osx-arm64.tar.gz` (Apple Silicon). Extract it and run
`./kustoplayground` (on Windows, `.\kustoplayground.exe`). Each contains one
Native AOT executable: no .NET SDK, runtime installation, or sibling DLLs needed.
The Linux binary targets glibc Linux (built on Ubuntu 24.04), not Alpine/musl.

```sh
./kustoplayground --help
./kustoplayground -e --query 'print Answer=6*7'
./kustoplayground --execute --json '{"Name":"Scores","Columns":[{"Name":"N","Type":"int"}],"Rows":[{"N":7}]}' --query 'Scores | summarize Total=sum(N)'
./kustoplayground -execute --csv 'City,Score
Oslo,3
Lima,9' --query 'Input | where Score > 5 | project City'
./kustoplayground -e --csv-file 'data/city scores.csv' --query 'Input | where Score > 5'
./kustoplayground -e --json-file scores.json --query 'Scores | count'
./kustoplayground --csv-path orders.csv --json-path products.json --query 'Input | join kind=inner (Products) on Sku'
```

`kustoplayground` with no arguments, `-h`, `--help`, and `-help` all print the
same usage and exit successfully. Otherwise choose **exactly one** of
`--query <KQL>` (one query) or `--mcp-server` (a persistent local database).
The execute prefix (`-e`, `--execute`, `-execute`) is optional.

Repeat or mix `--json`, `--csv` (inline text), `--json-path`, and `--csv-path`
(absolute or relative files). The existing `--json-file` and `--csv-file` names
are aliases. JSON uses the browser's table definition and its declared `Name`.
CSV has a header and supports quoted fields. CSVs, both inline and files, are
named `Input`, `Input2`, `Input3`, etc. in CSV argument order; intervening JSON
tables do not affect numbering. Duplicate table names are errors, never
overwrites. Omit all inputs for self-contained queries such as `print` or `datatable`.

Success writes one JSON object to stdout; the first item is the ordered KQL
schema (even for zero rows), followed by row arrays. For `print Answer=6*7`:

```json
{"status":200,"message":"Success","results":{"items":[{"Answer":"long"},[42]]},"duration":0}
```

`duration` is elapsed milliseconds and varies. Failures write JSON to stderr:
exit `2` for invalid commands or table input, `1` for query or output errors.

### Local MCP server

Configure a stdio MCP client to launch the native binary. For example, with
`orders.csv` containing a `Sku` column and `products.json` declaring a table
named `Products` with that same key:

```json
{
  "mcpServers": {
    "kusto-local": {
      "command": "/absolute/path/to/kustoplayground",
      "args": ["--mcp-server", "--csv-path", "/data/orders.csv", "--json-path", "/data/products.json"]
    }
  }
}
```

Use the executable for your platform and preferably absolute data paths; the
client controls the working directory. Call the single tool, `query`, with
`{"query":"Input | join kind=inner (Products) on Sku"}`. The server loads all
tables once before listening; each call runs against that same in-memory
database, with query-local `let` bindings. Results contain the same JSON
envelope as one-shot mode, as both structured content and text. KQL errors
set `isError` without ending the session. Stdout carries only MCP messages;
startup and query outcome logs go to stderr without logging KQL or rows.
Close stdin or press Ctrl+C to stop. No HTTP listener or Azure credentials are used.

## Build and test

With the .NET 11 SDK, run the unit tests from the repository root. They don't
need a browser or a WebAssembly workload:

```sh
dotnet test KustoPlayground.Core.Tests -c Release
dotnet test KustoPlayground.Cli.Tests -c Release
```

To run the same checks as CI on Windows, macOS, or Linux, install the WebAssembly
workload once, build with .NET and NUnit analyzer warnings treated as errors,
and run the browser suite with PowerShell 7.4 or newer:

```sh
dotnet workload install wasm-tools
dotnet build KustoPlayground.sln -c Release -warnaserror
pwsh -File test-browser.ps1
```

On a new Linux machine, use `pwsh -File test-browser.ps1 -WithDeps` to install
Chromium's system libraries as well. The script publishes the **Release AOT**
site, builds the separate Playwright project, installs its matching headless
Chromium, and runs the tests. They serve only the published files at the GitHub
Pages subpath, import CSV (paste and file upload) and JSON, and check the KQL
results, including grouped aggregation, typed parsing, schema inspection,
missing values, and expanding nested JSON and joining it to a lookup table.
No development server or external service is involved.

To build the CLI locally, install the [Native AOT build prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites)
for your OS (C++ build tools on Windows, Clang and zlib development files on
Linux, Xcode Command Line Tools on macOS). Publish for the current host and
smoke-test the **executable**, not `dotnet run`. For example, on x64 Linux:

```sh
dotnet publish KustoPlayground.Cli -c Release -r linux-x64 -o artifacts/native -warnaserror
pwsh -File test-cli.ps1 -Binary ./artifacts/native/kustoplayground
```

Use `-r win-x64` and `kustoplayground.exe` on Windows, or `-r osx-arm64` on
Apple Silicon. CI builds each target on its native runner. The smoke test copies
only the executable into an empty directory and checks help, JSON and CSV
queries, a gap-filled series, and a real MCP stdio session: discovery, a
three-table join, reuse after a query error, and clean shutdown on EOF.

## Deployment

CI makes the three CLI archives downloadable only after the unit tests,
analyzers, published-site Chromium tests, and **all three native executable
smoke tests** pass. It transfers the exact tested archives between jobs and
checks their SHA-256 hashes before upload. On a push to `main`, the deploy job
then publishes the browser-tested `github-pages` artifact to
[Kusto Playground](https://hixon10.github.io/KustoPlayground/) without rebuilding
it. After deployment, every successful push to `main` publishes those exact
CLI archives together in a new GitHub Release, tagged `cli-build-<run number>`.
The repository's **Releases → Latest** link always points to the newest published
build. Pull requests run the checks but cannot deploy or create releases.

The repository's **Settings > Pages > Build and deployment > Source** must be
**GitHub Actions**. The published site is not checked into Git. No manual file
copy or personal deployment token is needed.

## Supported String operators:
- [contains](https://learn.microsoft.com/en-us/kusto/query/contains-operator)
- [!contains](https://learn.microsoft.com/en-us/kusto/query/not-contains-operator)
- [startswith](https://learn.microsoft.com/en-us/kusto/query/startswith-operator)
- [!startswith](https://learn.microsoft.com/en-us/kusto/query/not-startswith-operator)
- [endswith](https://learn.microsoft.com/en-us/kusto/query/endswith-operator)
- [!endswith](https://learn.microsoft.com/en-us/kusto/query/not-endswith-operator)
- [matches regex](https://learn.microsoft.com/en-us/kusto/query/matches-regex-operator)
- Whole-term search: `has`, `has_cs`, their negations, `has_any`, and `has_all`
- `==`, `=~`, `!=`, `!~`

## Supported Tabular operators:
- [extend](https://learn.microsoft.com/en-us/kusto/query/extend-operator)
- [project](https://learn.microsoft.com/en-us/kusto/query/project-operator)
- [project-away](https://learn.microsoft.com/en-us/kusto/query/project-away-operator)
- [take](https://learn.microsoft.com/en-us/kusto/query/take-operator)
- [where](https://learn.microsoft.com/en-us/kusto/query/where-operator)
- [sort](https://learn.microsoft.com/en-us/kusto/query/sort-operator)
- [top](https://learn.microsoft.com/en-us/kusto/query/top-operator)
- [count](https://learn.microsoft.com/en-us/kusto/query/count-operator)
- [summarize](https://learn.microsoft.com/en-us/kusto/query/summarize-operator)
- [make-series](https://learn.microsoft.com/en-us/kusto/query/make-series-operator) (numeric, datetime, and timespan axes)
- [render](https://learn.microsoft.com/en-us/kusto/query/render-operator) (terminal `linechart` and `timechart`, without options)
- [distinct](https://learn.microsoft.com/en-us/kusto/query/distinct-operator)
- [join](https://learn.microsoft.com/en-us/kusto/query/join-operator) (inner, innerunique, outer, semi and anti joins)
- [print](https://learn.microsoft.com/en-us/kusto/query/print-operator)
- [parse](https://learn.microsoft.com/en-us/kusto/query/parse-operator) (`simple`, `relaxed`, or `regex` with flags)
- [getschema](https://learn.microsoft.com/en-us/kusto/query/getschema-operator) (column names, ordinals and types, even for empty inputs)
- [mv-expand](https://learn.microsoft.com/en-us/kusto/query/mv-expand-operator) and [mv-apply](https://learn.microsoft.com/en-us/kusto/query/mv-apply-operator)
- [evaluate bag_unpack](https://learn.microsoft.com/en-us/kusto/query/bag-unpack-plugin) (inferred or explicit output schema)

`summarize` supports grouping, conditional aggregates, row selectors such as
`arg_max`, collections, variance/covariance, and nearest-rank percentiles. For example:

```kusto
StormEvents
| summarize Events=count(), Damage=sum(DamageProperty) by State, Day=bin(StartTime, 1d)
| sort by Damage desc
```

Distinct counts and percentiles are computed exactly in memory. Shuffle hints do
not change local execution. The local `hll` state can be merged and counted within
a query; it is not an exchange format for Azure Data Explorer sketches.

`make-series` returns the grouping keys, one dynamic array per measure, and the
aligned axis array. For example, this produces `Total=[5,0,7,0]` and
`Hour=[0,1,2,3]`:

```kusto
datatable(Hour:int, Units:long)[0,2, 0,3, 2,7]
| make-series Total=sum(Units) on Hour from 0 to 4 step 1
```

Use `by` to build separate series, or omit bounds to infer them per group.
In `from ... to ... step`, a bin starts before the explicit stop but can
contain later events; the alternate `in range(start, stop, step)` includes
the stop bin. Use a typed null default and `series_fill_linear()` for
interpolation, or `mv-expand` to turn the arrays back into rows. Each
series is limited to 1,048,576 bins.

`bin(value, width)` accepts negative widths for numbers and timespans; for
example, `bin(25, -10)` returns `30`. A numeric width for a datetime is in
seconds. Unlike `bin`, `bin_at` requires a positive constant width and a
non-null constant anchor.

## Charts

Paste either query into the web playground; no table import is needed:

```kusto
datatable(X:long, Y:long)[0,2, 1,5, 2,3]
| sort by X asc
| render linechart
```

```kusto
datatable(T:datetime, Hits:long)[datetime(2026-01-01),2, datetime(2026-01-02),5, datetime(2026-01-04),3]
| sort by T asc
| render timechart
```

The first column is X: numeric for a line chart, UTC datetime for a time chart.
Each remaining numeric column is a measure on the same Y axis. The first
remaining string column, if present, groups each measure into one line per
distinct value. Other columns remain in the result rows below the chart.
The legend identifies each plotted series. Points follow query order within
each group; null or non-finite coordinates break only the affected lines.

Try the [grouped event timechart](samples/grouped-timechart.kql) for a complete
`make-series` example. It expands the arrays to typed rows with `mv-expand` and
projects `Timestamp, EventType, EventCount, Revenue`, drawing six lines:
counts and revenue for views, purchases, and refunds. Alternatively, use
`summarize ... by bin(...)` to produce rows directly. Unexpanded dynamic
arrays, additional chart types, and `with (...)` options are not supported.
The native CLI accepts the same queries and returns its usual JSON rows, not an image.

## Working with nested JSON

Declare a JSON import column as `"Type": "dynamic"` to store arrays, property
bags, scalars, and nulls together. Use dot or bracket access for nested values,
`gettype()` to inspect a value, and `mv-expand` to produce one row per element.
Array, set, and bag functions let you sort, combine, and update values without
expanding them. This example totals units sold across two orders:

```kusto
let Catalog = datatable(Sku:int, Item:string)[7, "Pen", 8, "Ink"];
datatable(Items:dynamic)[dynamic([{"sku":7,"units":2},{"sku":8,"units":1}]), dynamic([{"sku":7,"units":3}])]
| mv-expand Items
| project Sku=tolong(Items.sku), Units=tolong(Items.units)
| join kind=inner (Catalog) on Sku
| summarize Sold=sum(Units) by Item
```

Cast dynamic join keys to a scalar type first. Integer keys can match across
`int` and `long` without changing the types of their output columns.
Likewise, cast dynamic values before comparing them to each other or passing
them to `dcount`. To test membership in a different array on each row, use
`set_has_element(ArrayColumn, Value)`; a scalar `in (...)` list must be constant.

## Missing values

Omitted or explicitly null values are allowed in nullable columns. In a query,
a missing string reads as `""`; other scalar types keep their nulls. Use
`isempty()` for text and `isnull()` for typed nulls. Comparisons can return an
unknown boolean: for example, `long(null) == long(null)` is null, and
`where` keeps only rows whose predicate is true.

`distinct` and `summarize by` group null keys together. Equijoins also match
two null keys; in contrast, `in (subquery)` never matches a null key.
Use `sort by Score asc nulls last` to choose where missing values appear.

## Query-local names and functions

Use [`let` statements](https://learn.microsoft.com/en-us/kusto/query/let-statement)
for scalars, tables, views, and functions with scalar or tabular parameters. Names
last for one query. A name normally re-evaluates its expression; use `toscalar()`
or `materialize()` to reuse the same value or rows.

```kusto
let Bucket = (text:string) { bin(toint(trim(@"\s+", text)), 5) };
let Events = print Line="name=sensor;value= 14 ;";
Events
| parse Line with "name=" Sensor ";value=" Raw ";"
| project Label=strcat(Sensor, ":", Bucket(Raw))
```

## Supported Scalar operators:
- [between](https://learn.microsoft.com/en-us/kusto/query/between-operator)
- [!between](https://learn.microsoft.com/en-us/kusto/query/not-between-operator)
- [in](https://learn.microsoft.com/en-us/kusto/query/in-cs-operator)
- [in~](https://learn.microsoft.com/en-us/kusto/query/in-operator)
- [!in](https://learn.microsoft.com/en-us/kusto/query/not-in-cs-operator)
- [!in~](https://learn.microsoft.com/en-us/kusto/query/not-in-operator)

## Supported Scalar functions:
- `isnull()`, `isnotnull()`, `isempty()`, `isnotempty()`, `coalesce()`, `iff()`/`iif()`, and `case()`
- [base64_decode_tostring()](https://learn.microsoft.com/en-us/kusto/query/base64-decode-tostring-function)
- [base64_encode_tostring()](https://learn.microsoft.com/en-us/kusto/query/base64-encode-tostring-function)
- [ago()](https://learn.microsoft.com/en-us/kusto/query/ago-function)
- [now()](https://learn.microsoft.com/en-us/kusto/query/now-function)
- [totimespan()](https://learn.microsoft.com/en-us/kusto/query/totimespan-function)
- [make_timespan()](https://learn.microsoft.com/en-us/kusto/query/make-timespan-function)
- [todatetime()](https://learn.microsoft.com/en-us/kusto/query/todatetime-function)
- [url_encode()](https://learn.microsoft.com/en-us/kusto/query/url-encode-function)
- [url_decode()](https://learn.microsoft.com/en-us/kusto/query/url-decode-function)
- [toupper()](https://learn.microsoft.com/en-us/kusto/query/toupper-function)
- [tolower()](https://learn.microsoft.com/en-us/kusto/query/tolower-function)
- [strlen()](https://learn.microsoft.com/en-us/kusto/query/strlen-function)
- [bin()](https://learn.microsoft.com/en-us/kusto/query/bin-function) (also `floor()`) and [bin_at()](https://learn.microsoft.com/en-us/kusto/query/bin-at-function)
- [strcat()](https://learn.microsoft.com/en-us/kusto/query/strcat-function) and [trim()](https://learn.microsoft.com/en-us/kusto/query/trim-function)
- `toint()`, `tolong()`, `toreal()`/`todouble()`, `tostring()`, and `tobool()`/`toboolean()`
- [parse_json()](https://learn.microsoft.com/en-us/kusto/query/parse-json-function), [extract_json()](https://learn.microsoft.com/en-us/kusto/query/extract-json-function) (root, property and array-index paths, optional `typeof(...)`), and [pack_all()](https://learn.microsoft.com/en-us/kusto/query/pack-all-function)

## TODO
- create a table from free form text
- everything else...
