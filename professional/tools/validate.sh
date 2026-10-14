#!/usr/bin/env bash
# Requires .NET 10, wasm-tools, Python, and Chromium for Playwright.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts/validation
dotnet build samples/ProText.Desktop -c Release
dotnet run --project tests/ProText.Tests -c Release
dotnet publish samples/ProText.Browser -c Release -o artifacts/browser
python tools/browser_validate.py artifacts/browser
dotnet pack src/ProText.Core -c Release -o artifacts/packages
dotnet pack src/ProText.Avalonia -c Release -o artifacts/packages
python tools/verify_packages.py
