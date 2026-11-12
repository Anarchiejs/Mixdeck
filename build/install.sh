#!/usr/bin/env bash
# Install Mixdeck.app for the current user. Does not ask for a root password.
# On Windows, use Installer Mixdeck.cmd.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

if [[ "$(uname -s)" != Darwin ]]; then
  echo "build/install.sh installs the macOS app. On Windows, use Installer Mixdeck.cmd." >&2
  exit 1
fi

if [[ ! -d artifacts/Mixdeck.app ]]; then
  bash build/pack.sh --skip-tests "$@"
fi

mkdir -p "$HOME/Applications"
rm -rf "$HOME/Applications/Mixdeck.app"
cp -R artifacts/Mixdeck.app "$HOME/Applications/Mixdeck.app"
echo "Installed: $HOME/Applications/Mixdeck.app"
