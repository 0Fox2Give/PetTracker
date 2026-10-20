#!/usr/bin/env bash
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-$root/build/linux-x64}"
if [[ -e "$output" ]]; then
    printf 'Choose a new output directory: %s\n' "$output" >&2
    exit 1
fi
mkdir -p -- "$output"
output="$(cd -- "$output" && pwd)"

dotnet publish "$root/src/Linux/Linux.csproj" -c Release -r linux-x64 \
    --self-contained true -p:UseSharedCompilation=false -o "$output"
dotnet publish "$root/src/Worker/Worker.csproj" -c Release -r linux-x64 \
    --self-contained true -p:UseSharedCompilation=false -o "$output/capture"
mkdir -p -- "$output/docs"
cp -- "$root/LICENSE" "$root/NOTICE" "$root/THIRD-PARTY-NOTICES.txt" "$output/"
cp -R -- "$root/licenses" "$output/licenses"
cp -- "$root/docs/LINUX.md" "$output/docs/"
chmod +x -- "$output/PetTracker.Linux" "$output/capture/PetTracker.Capture"
printf '\nLinux preview built at %s\nSee docs/LINUX.md for capture permissions.\n' "$output"
