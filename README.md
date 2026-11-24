# Kusto Playground

**KustoPlayground** is a client-side playground for experimenting with [Kusto Query Language (KQL)](https://learn.microsoft.com/en-us/azure/data-explorer/kusto/query/).  

It runs entirely in the browser using **WebAssembly + Blazor + C#**, with no server dependencies.

## Features
- Create an **in-memory Kusto database** directly in your browser from JSON, or CSV.
- Write and run **KQL queries** interactively.
- 100% client-side - no data leaves your machine.
- Perfect for **learning, prototyping, and testing** queries quickly.

## Build and test

With the .NET 11 SDK, run the unit tests from the repository root. They don't
need a browser or a WebAssembly workload:

```sh
dotnet test KustoPlayground.Core.Tests -c Release
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
results. No development server or external service is involved.

## Deployment

CI packages `artifacts/site/wwwroot` as the `github-pages` artifact only after
the unit tests, analyzers, and Chromium tests pass. On a push to `main`, the
deploy job publishes that exact artifact to
[Kusto Playground](https://hixon10.github.io/KustoPlayground/) without rebuilding
it. Pull requests run the checks but cannot deploy.

The repository's **Settings > Pages > Build and deployment > Source** must be
**GitHub Actions**. The checked-in `docs` directory is the previous branch-based
release; there is no need to copy files there, edit the base path, or configure
a personal deployment token.

## Supported String operators:
- [contains](https://learn.microsoft.com/en-us/kusto/query/contains-operator)
- [!contains](https://learn.microsoft.com/en-us/kusto/query/not-contains-operator)
- [startswith](https://learn.microsoft.com/en-us/kusto/query/startswith-operator)
- [!startswith](https://learn.microsoft.com/en-us/kusto/query/not-startswith-operator)
- [endswith](https://learn.microsoft.com/en-us/kusto/query/endswith-operator)
- [!endswith](https://learn.microsoft.com/en-us/kusto/query/not-endswith-operator)
- [matches regex](https://learn.microsoft.com/en-us/kusto/query/matches-regex-operator)
- `==`, `=~`, `!=`, `!~`

## Supported Tabular operators:
- [extend](https://learn.microsoft.com/en-us/kusto/query/extend-operator)
- [project](https://learn.microsoft.com/en-us/kusto/query/project-operator)
- [take](https://learn.microsoft.com/en-us/kusto/query/take-operator)
- [where](https://learn.microsoft.com/en-us/kusto/query/where-operator)
- [sort](https://learn.microsoft.com/en-us/kusto/query/sort-operator)
- [count](https://learn.microsoft.com/en-us/kusto/query/count-operator)
- [distinct](https://learn.microsoft.com/en-us/kusto/query/distinct-operator)
- [print](https://learn.microsoft.com/en-us/kusto/query/print-operator)

## Supported Scalar operators:
- [between](https://learn.microsoft.com/en-us/kusto/query/between-operator)
- [!between](https://learn.microsoft.com/en-us/kusto/query/not-between-operator)
- [in](https://learn.microsoft.com/en-us/kusto/query/in-cs-operator)
- [in~](https://learn.microsoft.com/en-us/kusto/query/in-operator)
- [!in](https://learn.microsoft.com/en-us/kusto/query/not-in-cs-operator)
- [!in~](https://learn.microsoft.com/en-us/kusto/query/not-in-operator)

## Supported Scalar functions:
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

## TODO
- summarize operator (count(), dcount(), avg(), sum(), min(), max(), percentile(), percentiles())
- bin()
- strcat()
- trim()
- toint() / tolong() / toreal() / tostring() / tobool()
- parse operator
- parse_json() (maybe extract_json())
- pack_all()
- create a table from free form text
- getschema operator
- proper support of Nullable columns (filter, comparison operations, distinct, and so on)
- mv-expand operator
- `let` statement
- everything else...
