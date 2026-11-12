#!/usr/bin/env bash
# Build "Reelshelf Monitor.app" from release binaries and wrap it in a disk image. macOS only (lipo, sips, iconutil,
# codesign, hdiutil).
#
#   desktop/monitor/packaging/package.sh VERSION OUT_DIR BINARY [BINARY...]
#
# One BINARY per architecture; two are merged into a universal binary. Writes
# OUT_DIR/reelshelf-monitor-VERSION-macos-{universal,arm64,x86_64}.dmg and prints its path. Build the binaries with
# REELSHELF_APP_VERSION=VERSION, so the app sends the bundle's version as X-Reelshelf-Client: monitor/<version>.
#
# Signs ad hoc unless CODESIGN_IDENTITY names a Developer ID Application identity in the keychain (its name or SHA-1):
# then the app is signed with it and the hardened runtime, and the image is signed too, ready to notarize.
#
# Run by .github/workflows/publish-monitor.yml; by hand, from desktop/, for a build of this Mac's architecture:
#   version=$(monitor/packaging/version.sh)
#   REELSHELF_APP_VERSION=$version cargo build --release -p reelshelf-monitor
#   monitor/packaging/package.sh "$version" target/macos target/release/reelshelf-monitor
set -euo pipefail

if [ "$#" -lt 3 ]; then
  echo "usage: $0 VERSION OUT_DIR BINARY [BINARY...]" >&2
  exit 2
fi
version=$1
out_dir=$2
shift 2

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# The disk image's contents: the app, and a link to drag it onto.
app="$work/image/Reelshelf Monitor.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
ln -s /Applications "$work/image/Applications"

exe="$app/Contents/MacOS/reelshelf-monitor"
if [ "$#" -eq 1 ]; then
  cp "$1" "$exe"
else
  lipo -create -output "$exe" "$@"
fi
chmod 755 "$exe"

case "$(lipo -archs "$exe")" in
  *x86_64*arm64* | *arm64*x86_64*) arch=universal ;;
  *arm64*) arch=arm64 ;;
  *x86_64*) arch=x86_64 ;;
  *)
    echo "unexpected architectures in $exe: $(lipo -archs "$exe")" >&2
    exit 1
    ;;
esac

# The binary must call itself what the bundle says (`--version` prints the compiled-in REELSHELF_APP_VERSION).
built=$("$exe" --version)
if [ "$built" != "reelshelf-monitor $version" ]; then
  echo "the binary says \"$built\", not reelshelf-monitor $version: build it with REELSHELF_APP_VERSION=$version" >&2
  exit 1
fi

sed "s/@VERSION@/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist" > /dev/null

# AppIcon.icns from the full-size icon (icon.png, rendered from icon.svg), at every size the Finder and Dock ask for.
iconset="$work/AppIcon.iconset"
mkdir "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$here/icon.png" --out "$iconset/icon_${size}x${size}.png" > /dev/null
  sips -z "$((size * 2))" "$((size * 2))" "$here/icon.png" --out "$iconset/icon_${size}x${size}@2x.png" > /dev/null
done
iconutil --convert icns --output "$app/Contents/Resources/AppIcon.icns" "$iconset"

identity=${CODESIGN_IDENTITY:-}
if [ -n "$identity" ]; then
  # Developer ID: the hardened runtime and a secure timestamp are what notarization asks for.
  codesign --force --options runtime --timestamp --sign "$identity" "$app"
else
  # An ad-hoc signature over the whole bundle. Apple Silicon runs nothing unsigned, and without it a downloaded copy
  # is reported as damaged rather than merely unidentified.
  codesign --force --sign - "$app"
fi
codesign --verify --strict "$app"

mkdir -p "$out_dir"
dmg="$out_dir/reelshelf-monitor-$version-macos-$arch.dmg"
# hdiutil now and then fails with "Resource busy" on CI runners; a retry gets through.
for attempt in 1 2 3; do
  if hdiutil create -volname "Reelshelf Monitor" -srcfolder "$work/image" \
    -fs HFS+ -format UDZO -ov "$dmg" > /dev/null; then
    break
  fi
  if [ "$attempt" -eq 3 ]; then
    echo "hdiutil could not create $dmg" >&2
    exit 1
  fi
  sleep 5
done
if [ -n "$identity" ]; then
  codesign --force --timestamp --sign "$identity" "$dmg"
fi
echo "$dmg"
