#!/usr/bin/env bash
# Registers WarmupJoiner with your desktop (app menu + dock/taskbar icon).
# Run this from inside the extracted release folder - it expects the
# WarmupJoiner binary and app.png to sit right next to this script.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN_PATH="$SCRIPT_DIR/WarmupJoiner"
ICON_SRC="$SCRIPT_DIR/app.png"

if [[ ! -x "$BIN_PATH" ]]; then
    echo "error: no executable WarmupJoiner binary found at $BIN_PATH" >&2
    echo "Run this script from inside the extracted release folder." >&2
    exit 1
fi

if [[ ! -f "$ICON_SRC" ]]; then
    echo "error: app.png not found at $ICON_SRC" >&2
    exit 1
fi

ICON_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"
APPS_DIR="$HOME/.local/share/applications"
mkdir -p "$ICON_DIR" "$APPS_DIR"

cp "$ICON_SRC" "$ICON_DIR/warmupjoiner.png"

cat > "$APPS_DIR/warmupjoiner.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=WarmupJoiner
Comment=Finds a CS2 deathmatch server on warmupserver.net matching your preferences and joins it
Exec=$BIN_PATH
Icon=warmupjoiner
Terminal=false
Categories=Game;
StartupWMClass=WarmupJoiner
EOF

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache "$HOME/.local/share/icons/hicolor" >/dev/null 2>&1 || true

echo "Installed - WarmupJoiner should now show up in your app menu/dock with its icon."
echo "(If it doesn't appear right away, you may need to log out and back in.)"
