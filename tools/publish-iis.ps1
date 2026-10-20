# Builds a folder you can upload to a Windows Plesk subscription (IIS + ASP.NET Core Hosting Bundle 8).
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
dotnet publish lifehub/src/LifeHub/LifeHub.csproj -c Release -o publish
Write-Host "Upload the contents of ./publish to httpdocs (see docs/installation.md -> Plesk on Windows)."
