#Requires -Version 7.4
param([switch]$WithDeps)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Push-Location $PSScriptRoot
try {
    dotnet publish KustoPlayground.UI -c Release -o artifacts/site
    dotnet build KustoPlayground.BrowserTests -c Release -warnaserror

    $installArgs = @('install', '--only-shell', 'chromium')
    if ($WithDeps) { $installArgs += '--with-deps' }
    pwsh -NoProfile -File KustoPlayground.BrowserTests/bin/Release/net11.0/playwright.ps1 @installArgs

    dotnet test KustoPlayground.BrowserTests -c Release --no-build
}
finally {
    Pop-Location
}
