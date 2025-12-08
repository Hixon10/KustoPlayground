#!/usr/bin/env bash
set -o errexit
set -o pipefail

if [ "$1" = "test" ]; then
    dotnet clean && dotnet build && dotnet test
elif [ "$1" = "publish" ]; then
    dotnet clean && dotnet build -c Release && dotnet publish
else
    echo "Usage: $0 {test|publish}"
    exit 1
fi