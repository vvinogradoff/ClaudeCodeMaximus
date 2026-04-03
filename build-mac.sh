#!/bin/bash
# Build and package Tessyn Desktop as a macOS .app bundle
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
APP_DIR="$HOME/Applications/Tessyn Desktop.app"
CONTENTS="$APP_DIR/Contents"
MACOS="$CONTENTS/MacOS"
RESOURCES="$CONTENTS/Resources"
PUBLISH_DIR="/tmp/tessyn-desktop-publish"

echo "Building Tessyn Desktop..."
dotnet publish "$SCRIPT_DIR/code/TessynDesktop/TessynDesktop.csproj" \
    -c Release -r osx-arm64 --self-contained -o "$PUBLISH_DIR"

echo "Creating app bundle at $APP_DIR..."
rm -rf "$APP_DIR"
mkdir -p "$MACOS" "$RESOURCES"
cp -R "$PUBLISH_DIR/"* "$MACOS/"

# Rename binary and create launcher for PATH inheritance
mv "$MACOS/ClaudeMaximus" "$MACOS/TessynDesktop.bin"

cat > "$MACOS/TessynDesktop" << 'LAUNCHER'
#!/bin/bash
DIR="$(dirname "$0")"
# Source user's shell profile to get PATH (nvm, homebrew, claude, tessyn)
if [ -f "$HOME/.zshrc" ]; then
    source "$HOME/.zshrc" 2>/dev/null
elif [ -f "$HOME/.bash_profile" ]; then
    source "$HOME/.bash_profile" 2>/dev/null
fi
exec "$DIR/TessynDesktop.bin" "$@"
LAUNCHER
chmod +x "$MACOS/TessynDesktop"

cat > "$CONTENTS/Info.plist" << 'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Tessyn Desktop</string>
    <key>CFBundleDisplayName</key>
    <string>Tessyn Desktop</string>
    <key>CFBundleIdentifier</key>
    <string>com.tessyn.desktop</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleExecutable</key>
    <string>TessynDesktop</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
PLIST

rm -rf "$PUBLISH_DIR"

echo ""
echo "Done! Tessyn Desktop installed to $APP_DIR"
echo "You can open it from Finder, Spotlight, or: open '$APP_DIR'"
