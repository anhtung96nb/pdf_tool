param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
# Legacy entry point now exercises LLM extraction, source adapters, UI and Excel.
& (Join-Path $PSScriptRoot 'Run-LlmExtractionTests.ps1') -Configuration $Configuration