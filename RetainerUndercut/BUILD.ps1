$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "BUILD_DEV.ps1") -Configuration Release
