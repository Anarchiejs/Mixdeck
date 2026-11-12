#!/usr/bin/env bash
# Package the Linux or macOS app: portable tests, a self-contained publish, a tarball
# (and a minimal .app on macOS), then Velopack when vpk can run on this machine.
# Windows packaging stays in build/pack.ps1.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

version=""
rid=""
skip_tests=0
skip_pack=0
require_vpk=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      version="${2:-}"
      shift 2
      ;;
    --rid)
      rid="${2:-}"
      shift 2
      ;;
    --skip-tests) skip_tests=1; shift ;;
    --skip-pack) skip_pack=1; shift ;;
    --require-vpk) require_vpk=1; shift ;;
    *)
      echo "Unknown argument: $1" >&2
      echo "Usage: build/pack.sh [--version 0.1.0] [--rid linux-x64] [--skip-tests] [--skip-pack] [--require-vpk]" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$version" ]]; then
  version="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' Directory.Build.props | head -n 1)"
fi
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid version: $version" >&2
  exit 2
fi

if [[ -z "$rid" ]]; then
  case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) rid=linux-x64 ;;
    Linux-aarch64|Linux-arm64) rid=linux-arm64 ;;
    Darwin-arm64) rid=osx-arm64 ;;
    Darwin-x86_64) rid=osx-x64 ;;
    *)
      echo "Set --rid (linux-x64, linux-arm64, osx-arm64, or osx-x64)." >&2
      exit 2
      ;;
  esac
fi

if [[ "$skip_tests" -eq 0 ]]; then
  dotnet test Mixdeck.Portable.slnx -c Release -nologo
fi

publish="publish/$rid"
rm -rf "$publish"
dotnet publish src/Mixdeck.Desktop/Mixdeck.Desktop.csproj -c Release -r "$rid" --self-contained \
  -p:Version="$version" -o "$publish"
chmod +x "$publish/Mixdeck"

mkdir -p artifacts
archive="artifacts/Mixdeck-$version-$rid.tar.gz"
if [[ "$rid" == osx-* ]]; then
  app="artifacts/Mixdeck.app"
  rm -rf "$app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$publish"/. "$app/Contents/MacOS/"
  cat > "$app/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key>
  <string>com.mixdeck.desktop</string>
  <key>CFBundleName</key>
  <string>Mixdeck</string>
  <key>CFBundleDisplayName</key>
  <string>Mixdeck</string>
  <key>CFBundleExecutable</key>
  <string>Mixdeck</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$version</string>
  <key>CFBundleVersion</key>
  <string>$version</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF
  tar -C artifacts -czf "$archive" Mixdeck.app
else
  tar -C "$publish" -czf "$archive" .
fi
echo "Archive: $archive"

if [[ "$skip_pack" -eq 1 ]]; then
  exit 0
fi

fail_vpk() {
  if [[ "$require_vpk" -eq 1 ]]; then
    echo "Velopack packaging failed." >&2
    exit 1
  fi
  echo "Velopack packaging was skipped: $1"
  exit 0
}

if ! dotnet tool restore; then
  fail_vpk "dotnet tool restore failed"
fi

icon="src/Mixdeck.App/Assets/Mixdeck-256.png"
if [[ "$rid" == linux-* ]]; then
  dotnet tool run vpk -- '[linux]' pack \
    --packId MixdeckApp \
    --packVersion "$version" \
    --packDir "$publish" \
    --mainExe Mixdeck \
    --packTitle Mixdeck \
    --icon "$icon" \
    --categories "AudioVideo;Audio;" \
    --outputDir artifacts \
    || fail_vpk "vpk [linux] pack failed"
elif [[ "$(uname -s)" == Darwin ]]; then
  iconset="$(mktemp -d)/Mixdeck.iconset"
  mkdir -p "$iconset"
  sips -z 16 16 "$icon" --out "$iconset/icon_16x16.png" >/dev/null
  sips -z 32 32 "$icon" --out "$iconset/icon_16x16@2x.png" >/dev/null
  sips -z 32 32 "$icon" --out "$iconset/icon_32x32.png" >/dev/null
  sips -z 64 64 "$icon" --out "$iconset/icon_32x32@2x.png" >/dev/null
  sips -z 128 128 "$icon" --out "$iconset/icon_128x128.png" >/dev/null
  sips -z 256 256 "$icon" --out "$iconset/icon_128x128@2x.png" >/dev/null
  sips -z 256 256 "$icon" --out "$iconset/icon_256x256.png" >/dev/null
  sips -z 512 512 "$icon" --out "$iconset/icon_256x256@2x.png" >/dev/null
  sips -z 512 512 "$icon" --out "$iconset/icon_512x512.png" >/dev/null
  icns="artifacts/Mixdeck.icns"
  iconutil -c icns "$iconset" -o "$icns"
  dotnet tool run vpk -- '[osx]' pack \
    --packId MixdeckApp \
    --packVersion "$version" \
    --packDir "$publish" \
    --mainExe Mixdeck \
    --packTitle Mixdeck \
    --icon "$icns" \
    --outputDir artifacts \
    || fail_vpk "vpk [osx] pack failed"
else
  fail_vpk "vpk [osx] pack only runs on macOS"
fi

echo "Velopack packages are in artifacts/"
