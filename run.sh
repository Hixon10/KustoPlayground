#!/usr/bin/env bash
set -o errexit
set -o pipefail

if [ "$1" = "test" ]; then
    dotnet build KustoPlayground.sln -c Release -warnaserror && dotnet test KustoPlayground.Core.Tests -c Release --no-build
elif [ "$1" = "publish" ]; then
    dotnet publish KustoPlayground.UI -c Release -o artifacts/site
else
    echo "Usage: $0 {test|publish}"
    exit 1
fi