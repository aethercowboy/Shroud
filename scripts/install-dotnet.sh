#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
INSTALL_SCRIPT_PATH="${DOTNET_INSTALL_SCRIPT_PATH:-/tmp/dotnet-install.sh}"

if ! command -v curl >/dev/null 2>&1; then
  echo "curl is required to install .NET." >&2
  exit 1
fi

if [[ ! -f "$INSTALL_SCRIPT_PATH" ]]; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$INSTALL_SCRIPT_PATH"
fi

SDK_VERSION="$(python3 - <<'PY' "$REPO_ROOT/global.json"
import json, sys
with open(sys.argv[1], 'r', encoding='utf-8') as f:
    data = json.load(f)
print(data['sdk']['version'])
PY
)"

bash "$INSTALL_SCRIPT_PATH" --version "$SDK_VERSION" --install-dir "$INSTALL_DIR"

mapfile -t TARGET_FRAMEWORKS < <(python3 - <<'PY' "$REPO_ROOT"
from pathlib import Path
import xml.etree.ElementTree as ET
import re

root = Path(__import__('sys').argv[1])
frameworks = set()
for csproj in root.rglob('*.csproj'):
    tree = ET.parse(csproj)
    for node_name in ('TargetFramework', 'TargetFrameworks'):
        for node in tree.findall(f'.//{node_name}'):
            values = [v.strip() for v in (node.text or '').split(';') if v.strip()]
            frameworks.update(values)

for framework in sorted(frameworks):
    match = re.match(r'net(\d+)\.\d+$', framework)
    if match:
        print(match.group(1) + '.0')
PY
)

for channel in "${TARGET_FRAMEWORKS[@]}"; do
  if [[ "$channel" == "10.0" ]]; then
    continue
  fi

  bash "$INSTALL_SCRIPT_PATH" --channel "$channel" --runtime dotnet --install-dir "$INSTALL_DIR"
done

echo
echo "Installed .NET into: $INSTALL_DIR"
echo "Add to PATH for current shell: export PATH=\"$INSTALL_DIR:\$PATH\""
