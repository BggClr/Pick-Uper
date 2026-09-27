#!/usr/bin/env bash
#
# Publishes pick-uper as a single self-contained Windows exe with all
# dependencies bundled, from macOS/Linux (cross-compile) or Windows.
#
# The project file already turns on SelfContained/PublishSingleFile/
# PublishTrimmed/PublishReadyToRun whenever a RuntimeIdentifier is supplied,
# so a normal run produces one pick-uper.exe with no loose DLLs and no .NET
# runtime dependency on the target machine.
#
# Usage:
#   ./scripts/build.sh                    # win-x64, Release, ReadyToRun
#   ./scripts/build.sh -r win-arm64        # target ARM64 Windows instead
#   ./scripts/build.sh -c                  # smaller, compressed exe (no R2R)
#   ./scripts/build.sh -a                  # Native AOT (~5ms startup instead of ~40ms)
#   ./scripts/build.sh -o dist             # custom output directory
#
# Native AOT (-a) needs the MSVC linker (link.exe), which only exists on
# Windows — it cannot be cross-compiled from macOS/Linux. Run this script
# with -a from a Windows machine (PowerShell's bash, WSL, or Git Bash all
# work, as long as the .NET SDK and Visual Studio Build Tools are installed).

set -euo pipefail

RUNTIME="win-x64"
CONFIGURATION="Release"
OUTPUT_DIR=""
COMPRESSED=0
AOT=0

usage() {
    grep '^#' "$0" | sed '1d' | sed 's/^# \{0,1\}//'
    exit 1
}

while getopts "r:o:cah" opt; do
    case "$opt" in
        r) RUNTIME="$OPTARG" ;;
        o) OUTPUT_DIR="$OPTARG" ;;
        c) COMPRESSED=1 ;;
        a) AOT=1 ;;
        h) usage ;;
        *) usage ;;
    esac
done

if [[ "$COMPRESSED" -eq 1 && "$AOT" -eq 1 ]]; then
    echo "error: -c (compressed single-file) and -a (Native AOT) are mutually exclusive" >&2
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/src/PickUper/PickUper.csproj"

if [[ -z "$OUTPUT_DIR" ]]; then
    OUTPUT_DIR="$REPO_ROOT/artifacts/publish/$RUNTIME"
fi

PUBLISH_ARGS=(publish "$PROJECT" -c "$CONFIGURATION" -r "$RUNTIME" -o "$OUTPUT_DIR")

if [[ "$COMPRESSED" -eq 1 ]]; then
    PUBLISH_ARGS+=(-p:PublishReadyToRun=false -p:EnableCompressionInSingleFile=true)
fi

if [[ "$AOT" -eq 1 ]]; then
    case "$(uname -s)" in
        MINGW* | MSYS* | CYGWIN*) ;;
        *)
            if [[ ! -d "/mnt/c/Windows" ]]; then
                echo "warning: Native AOT needs the MSVC linker, which only ships with Windows." >&2
                echo "         This looks like a non-Windows host, so the build will likely fail." >&2
            fi
            ;;
    esac

    # PublishReadyToRun and PublishAot are mutually exclusive; -p: on the
    # command line overrides the csproj's own ReadyToRun-when-RID default.
    PUBLISH_ARGS+=(-p:PublishAot=true -p:PublishReadyToRun=false)
fi

echo "Publishing $RUNTIME build to $OUTPUT_DIR..."
dotnet "${PUBLISH_ARGS[@]}"

echo "Built $OUTPUT_DIR/pick-uper.exe"
