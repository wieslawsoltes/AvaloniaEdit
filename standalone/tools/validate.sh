#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tests/Scribe.Core.Tests -c Release
dotnet build samples/Scribe.Desktop -c Release
dotnet run --project tests/Scribe.UI.Tests -c Release
dotnet publish samples/Scribe.Browser -c Release -o artifacts/browser
python tools/browser_validate.py artifacts/browser
dotnet pack src/Scribe.Core -c Release -o artifacts/packages
dotnet pack src/Scribe.Avalonia -c Release -o artifacts/packages
python tools/verify_independence.py
python tools/verify_packages.py
