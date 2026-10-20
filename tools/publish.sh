#!/usr/bin/env bash
# Framework-dependent publish for Linux hosts (Plesk .NET Toolkit, systemd, etc.).
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet publish lifehub/src/LifeHub/LifeHub.csproj -c Release -o publish /p:UseAppHost=false
echo "Published to ./publish — start with: LIFEHUB_DATA=/path/to/data dotnet publish/LifeHub.dll"
