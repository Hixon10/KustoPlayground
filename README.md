# Kusto Playground

**KustoPlayground** is a client-side playground for experimenting with [Kusto Query Language (KQL)](https://learn.microsoft.com/en-us/azure/data-explorer/kusto/query/).  

It runs entirely in the browser using **WebAssembly + Blazor + C#**, with no server dependencies.

## Features
- Create an **in-memory Kusto database** directly in your browser from JSON, or CSV.
- Write and run **KQL queries** interactively.
- 100% client-side - no data leaves your machine.
- Perfect for **learning, prototyping, and testing** queries quickly.

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

## TODO
- summarize operator (count(), dcount(), avg(), sum(), min(), max(), percentile(), percentiles())
- bin()
- toupper() / tolower()
- strlen()
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
