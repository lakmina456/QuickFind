$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

Write-Host "Publishing QuickFind (win-x64, self-contained)..."
dotnet publish .\QuickFind.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\bin\Release\net8.0-windows\win-x64\publish

Write-Host ""
Write-Host "Publish done."
Write-Host "Compile installer\QuickFind.iss with Inno Setup to create the setup EXE."
Write-Host "Autostart is OFF by default in the installer (optional task)."
