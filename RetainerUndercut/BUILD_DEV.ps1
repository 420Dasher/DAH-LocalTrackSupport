param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "RetainerUndercut.csproj"

Write-Host "== Retainer Undercut v0.1.0 ==" -ForegroundColor Cyan
Write-Host "Project: $Root"

Push-Location $Root
try {
    dotnet --version
    if ($LASTEXITCODE -ne 0) { throw "dotnet --version failed with exit code $LASTEXITCODE." }

    dotnet restore $Project
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    dotnet build $Project -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE. See the compiler error(s) above." }

    $ExpectedDll = Join-Path $Root "bin\$Configuration\RetainerUndercut.dll"
    if (Test-Path $ExpectedDll) {
        $Dll = $ExpectedDll
    }
    else {
        $Dll = Get-ChildItem -Path (Join-Path $Root "bin\$Configuration") -Filter "RetainerUndercut.dll" -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }

    if (-not $Dll -or -not (Test-Path $Dll)) {
        throw "Build succeeded, but RetainerUndercut.dll could not be located under bin\$Configuration."
    }

    # Dalamud dev-plugin icons are loaded from images\icon.png NEXT TO THE BUILT DLL.
    # Copy it explicitly so loading bin\Debug as the dev-plugin location always has the icon.
    $DllDir = Split-Path -Parent $Dll
    $OutputImages = Join-Path $DllDir "images"
    $SourceIcon = Join-Path $Root "images\icon.png"
    $OutputIcon = Join-Path $OutputImages "icon.png"
    New-Item -ItemType Directory -Force -Path $OutputImages | Out-Null
    Copy-Item -Force $SourceIcon $OutputIcon
    if (-not (Test-Path $OutputIcon)) {
        throw "Build succeeded, but the dev-plugin icon was not copied to $OutputIcon."
    }

    Write-Host ""
    Write-Host "BUILD SUCCESS" -ForegroundColor Green
    Write-Host "Dev-plugin DLL: $Dll" -ForegroundColor Green
    Write-Host "Dev-plugin icon: $OutputIcon" -ForegroundColor Green
    Write-Host ""
    Write-Host "Dalamud: /xlsettings -> Experimental -> Dev Plugin Locations"
    Write-Host "Add: $(Split-Path -Parent $Dll)"
    Write-Host "Then load 'Retainer Undercut' and use /rundercut"
    Write-Host "Expected window title: Retainer Undercut - v0.1.0" -ForegroundColor Yellow
}
finally {
    Pop-Location
}
