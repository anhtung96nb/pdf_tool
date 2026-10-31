param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [ValidateSet('x86', 'x64')][string]$Architecture = 'x86')
# Legacy entry point: semantic rule extraction has been removed.
# Architecture is retained for script compatibility; the LLM suite uses x64 Pdfium.
& (Join-Path $PSScriptRoot 'Run-LlmExtractionTests.ps1') -Configuration $Configuration
