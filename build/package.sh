#!/usr/bin/env bash
# Builds the release packages for one platform:
#   build/package.sh <version> <runtime-id> [output-dir]
# e.g. build/package.sh 1.0.0 linux-x64 dist
# Produces mcdiscovery-api-<version>-<rid> and mcdiscovery-scanner-<version>-<rid> archives (.zip for
# Windows, .tar.gz otherwise). Both are self-contained single-file executables: no .NET install needed.
# The website must be built first (cd web && npm ci && npx ng build).
set -euo pipefail

version="${1:?version}"
rid="${2:?runtime id, e.g. linux-x64}"
out="$(mkdir -p "${3:-dist}" && cd "${3:-dist}" && pwd)"
root="$(cd "$(dirname "$0")/.." && pwd)"
web="$root/web/dist/web/browser"
staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT

[[ -f "$web/index.html" ]] || { echo "Website not built: run 'cd web && npm ci && npx ng build' first" >&2; exit 1; }

publish() {
  dotnet publish "$root/src/$1" -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none -p:Version="$version" -o "$2"
}

archive() { # <dir> <name>
  if [[ "$rid" == win-* ]]; then
    (cd "$1" && zip -qr "$out/$2.zip" .)
  else
    tar -czf "$out/$2.tar.gz" -C "$1" .
  fi
}

docs() { cp "$root/LICENSE" "$root/NOTICE" "$root/README.md" "$1/"; }

api="$staging/mcdiscovery-api-$version-$rid"
publish McDiscovery.Api "$api"
rm -rf "$api/wwwroot" "$api/appsettings.Development.json" && cp -r "$web" "$api/wwwroot"
docs "$api"
archive "$api" "mcdiscovery-api-$version-$rid"

scanner="$staging/mcdiscovery-scanner-$version-$rid"
publish McDiscovery.Scanner "$scanner"
mkdir -p "$scanner/config" && cp "$root/config/targets.txt" "$root/config/exclude.txt" "$scanner/config/"
docs "$scanner"
archive "$scanner" "mcdiscovery-scanner-$version-$rid"

echo "Packages for $rid written to $out"
