#!/usr/bin/env bash
set -euo pipefail

INSTALL_PATH="/usr/local/bin/openapidyalog"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Detect RID
case "$(uname -m)" in
  x86_64)  RID="linux-x64"   ;;
  aarch64) RID="linux-arm64" ;;
  *)
    echo "Unsupported architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

echo "Building for $RID..."
dotnet publish "$SCRIPT_DIR/src/OpenAPIDyalog/OpenAPIDyalog.csproj" \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -c Debug \
  -o "$SCRIPT_DIR/publish/$RID" \
  --nologo

echo "Installing to $INSTALL_PATH..."
install -m 755 "$SCRIPT_DIR/publish/$RID/OpenAPIDyalog" "$INSTALL_PATH"

echo "Done. Run: openapidyalog --help"
