#!/usr/bin/env bash
# Install the current user copy. Does not ask for a root password.
# Linux: ~/.local/lib/mixdeck and an XDG desktop entry.
# macOS: ~/Applications/Mixdeck.app
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

if [[ ! -d publish ]]; then
  bash build/pack.sh --skip-tests "$@"
fi

case "$(uname -s)" in
  Linux)
    rid="linux-$(uname -m | sed 's/x86_64/x64/; s/aarch64/arm64/')"
    source="publish/$rid"
    if [[ ! -x "$source/Mixdeck" ]]; then
      echo "No published build in $source. Run build/pack.sh first." >&2
      exit 1
    fi
    dest="$HOME/.local/lib/mixdeck"
    mkdir -p "$dest" "$HOME/.local/bin" "$HOME/.local/share/applications"
    rm -rf "$dest"
    mkdir -p "$dest"
    cp -a "$source"/. "$dest/"
    ln -sfn "$dest/Mixdeck" "$HOME/.local/bin/mixdeck"
    cat > "$HOME/.local/share/applications/mixdeck.desktop" << EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=Mixdeck
Comment=System audio control
Exec=$dest/Mixdeck
Icon=$root/src/Mixdeck.App/Assets/Mixdeck-256.png
Terminal=false
Categories=AudioVideo;Audio;
EOF
    echo "Installed: $HOME/.local/bin/mixdeck"
    ;;
  Darwin)
    app="artifacts/Mixdeck.app"
    if [[ ! -d "$app" ]]; then
      echo "No Mixdeck.app in artifacts/. Run build/pack.sh on this Mac first." >&2
      exit 1
    fi
    mkdir -p "$HOME/Applications"
    rm -rf "$HOME/Applications/Mixdeck.app"
    cp -R "$app" "$HOME/Applications/Mixdeck.app"
    echo "Installed: $HOME/Applications/Mixdeck.app"
    ;;
  *)
    echo "On Windows, use Installer Mixdeck.cmd." >&2
    exit 1
    ;;
esac
