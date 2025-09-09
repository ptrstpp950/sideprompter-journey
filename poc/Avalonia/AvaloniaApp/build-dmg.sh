#!/usr/bin/env bash
set -euo pipefail

# build-dmg.sh
# Produce a self-contained universal macOS .app by publishing both osx-x64 and osx-arm64
# and lipo-combining native binaries (app host and MonoBundle/*.dylib).

echo "== build-dmg: starting universal macOS build =="

command -v dotnet >/dev/null 2>&1 || { echo "dotnet not found in PATH. Install .NET SDK." >&2; exit 1; }
command -v lipo >/dev/null 2>&1 || { echo "lipo not found in PATH. Install Xcode command-line tools." >&2; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Find project file
PROJ_FILE="" 
for f in *.csproj; do
  PROJ_FILE="$f"
  break
done
if [[ -z "$PROJ_FILE" ]]; then
  echo "No .csproj found in $SCRIPT_DIR. Please run this script from the project directory." >&2;
  exit 1
fi

echo "Using project: $PROJ_FILE"

TMP_DIR="$(mktemp -d)"
PUBLISH_DIR="$TMP_DIR/publish"
STAGE_DIR="$TMP_DIR/stage"
OUTPUT_DIR="$SCRIPT_DIR/dist"
mkdir -p "$PUBLISH_DIR" "$STAGE_DIR" "$OUTPUT_DIR"

# Publish self-contained builds for both RIDs
echo "Publishing osx-x64 (self-contained)..."
dotnet publish "$PROJ_FILE" -c Release -r osx-x64 --self-contained true -o "$PUBLISH_DIR/osx-x64"

echo "Publishing osx-arm64 (self-contained)..."
dotnet publish "$PROJ_FILE" -c Release -r osx-arm64 --self-contained true -o "$PUBLISH_DIR/osx-arm64"

# Locate .app bundles
ARM64_APP="$(find "$PUBLISH_DIR/osx-arm64" -maxdepth 3 -name "*.app" -print -quit || true)"
X64_APP="$(find "$PUBLISH_DIR/osx-x64" -maxdepth 3 -name "*.app" -print -quit || true)"

if [[ -z "$ARM64_APP" || -z "$X64_APP" ]]; then
  echo "Could not find .app in publish outputs. ARM64_APP=$ARM64_APP X64_APP=$X64_APP" >&2
  exit 1
fi

APP_NAME="$(basename "$ARM64_APP")"
echo "Found app: $APP_NAME"

# Use arm64 build as base
echo "Preparing staging area using arm64 app as base..."
cp -R "$ARM64_APP" "$STAGE_DIR/"
FINAL_APP="$STAGE_DIR/$APP_NAME"

# Helper: lipo combine two files if both present, otherwise copy arm64 file
lipo_combine_or_copy() {
  local relpath="$1"   # path relative to .app root, e.g., Contents/MacOS/MyApp
  local src_x64="$PUBLISH_DIR/osx-x64/$APP_NAME/$relpath"
  local src_arm="$PUBLISH_DIR/osx-arm64/$APP_NAME/$relpath"
  local dest="$FINAL_APP/$relpath"

  if [[ -f "$src_x64" && -f "$src_arm" ]]; then
    echo "Lipo combining $relpath"
    mkdir -p "$(dirname "$dest")" 
    lipo -create "$src_x64" "$src_arm" -output "$dest"
    chmod --reference="$src_arm" "$dest" || true
  elif [[ -f "$src_arm" ]]; then
    echo "Copying arm64-only $relpath"
    mkdir -p "$(dirname "$dest")"
    cp -p "$src_arm" "$dest"
  elif [[ -f "$src_x64" ]]; then
    echo "Copying x64-only $relpath"
    mkdir -p "$(dirname "$dest")"
    cp -p "$src_x64" "$dest"
  else
    echo "No source for $relpath found in either build; skipping." >&2
  fi
}

# 1) Combine the app host (Contents/MacOS/*)
APP_HOST_X64="$(find "$PUBLISH_DIR/osx-x64/$APP_NAME/Contents/MacOS" -type f -maxdepth 1 -print -quit || true)"
APP_HOST_ARM="$(find "$PUBLISH_DIR/osx-arm64/$APP_NAME/Contents/MacOS" -type f -maxdepth 1 -print -quit || true)"

if [[ -n "$APP_HOST_X64" && -n "$APP_HOST_ARM" ]]; then
  HOST_BASENAME="$(basename "$APP_HOST_ARM")"
  echo "Combining app host $HOST_BASENAME"
  lipo -create "$APP_HOST_X64" "$APP_HOST_ARM" -output "$FINAL_APP/Contents/MacOS/$HOST_BASENAME"
  chmod +x "$FINAL_APP/Contents/MacOS/$HOST_BASENAME"
else
  echo "App host not found in one of the builds; copying available host"
  if [[ -n "$APP_HOST_ARM" ]]; then
    cp -p "$APP_HOST_ARM" "$FINAL_APP/Contents/MacOS/"
  elif [[ -n "$APP_HOST_X64" ]]; then
    cp -p "$APP_HOST_X64" "$FINAL_APP/Contents/MacOS/"
  fi
fi

# 2) Combine native libraries in Contents/MonoBundle/
MONOBUNDLE_SUBPATH="Contents/MonoBundle"
ARM_MB_DIR="$PUBLISH_DIR/osx-arm64/$APP_NAME/$MONOBUNDLE_SUBPATH"
X64_MB_DIR="$PUBLISH_DIR/osx-x64/$APP_NAME/$MONOBUNDLE_SUBPATH"
FINAL_MB_DIR="$FINAL_APP/$MONOBUNDLE_SUBPATH"

if [[ -d "$ARM_MB_DIR" || -d "$X64_MB_DIR" ]]; then
  mkdir -p "$FINAL_MB_DIR"
  # iterate over unique filenames in both dirs
  pushd "$TMP_DIR" >/dev/null
  mapfile -t files < <( (cd "$ARM_MB_DIR" 2>/dev/null && ls -1) 2>/dev/null || true; (cd "$X64_MB_DIR" 2>/dev/null && ls -1) 2>/dev/null || true ) || true
  # make unique
  files=( $(printf "%s\n" "${files[@]}" | sort -u) )
  for f in "${files[@]}"; do
    case "$f" in
      *.dylib|*.so|*.bundle)
        echo "Processing native file: $f"
        lipo_combine_or_copy "$MONOBUNDLE_SUBPATH/$f"
        ;; 
      *)
        # Copy other files as-is from arm if present, else x64
        if [[ -f "$ARM_MB_DIR/$f" ]]; then
          cp -p "$ARM_MB_DIR/$f" "$FINAL_MB_DIR/"
        elif [[ -f "$X64_MB_DIR/$f" ]]; then
          cp -p "$X64_MB_DIR/$f" "$FINAL_MB_DIR/"
        fi
        ;;
    esac
  done
  popd >/dev/null
else
  echo "No MonoBundle found in either build; skipping MonoBundle merging"
fi

# 3) Merge any other known native locations if present (Plugins, runtimes/native, etc.)
# Common locations to check - adjust as needed
for loc in "Contents/Frameworks" "Contents/Resources" "Contents/Plugins"; do
  SRC_ARM_DIR="$PUBLISH_DIR/osx-arm64/$APP_NAME/$loc"
  SRC_X64_DIR="$PUBLISH_DIR/osx-x64/$APP_NAME/$loc"
  if [[ -d "$SRC_ARM_DIR" || -d "$SRC_X64_DIR" ]]; then
    echo "Merging native files from $loc"
    # find files with native extensions and lipo-combine
    if [[ -d "$SRC_ARM_DIR" ]]; then
      pushd "$SRC_ARM_DIR" >/dev/null
      for f in $(find . -type f \n\(-name '*.dylib' -o -name '*.so' -o -name '*.bundle' \) -print 2>/dev/null); do
        rel="${f#./}"
        lipo_combine_or_copy "$loc/$rel"
      done
      popd >/dev/null
    fi
  fi
done

# Ensure executable bit preserved on the app host (already set above)

# 4) Codesign if requested (preserve existing behavior if environment vars set)
if [[ -n "${CODESIGN_ID:-}" ]]; then
  echo "Codesigning $FINAL_APP with identity $CODESIGN_ID"
  if command -v codesign >/dev/null 2>&1; then
    codesign --deep --force --verbose --sign "$CODESIGN_ID" "$FINAL_APP" || echo "codesign failed (continuing)" >&2
  else
    echo "codesign not available; skipping" >&2
  fi
fi

# 5) Create DMG
DMG_NAME="$OUTPUT_DIR/${APP_NAME%.app}-universal.dmg"
echo "Creating DMG: $DMG_NAME"
hdiutil create -volname "${APP_NAME%.app}" -srcfolder "$FINAL_APP" -ov -format UDZO "$DMG_NAME"

# Move DMG to project dir (OUTPUT_DIR already in project dir)

# Cleanup
if [[ -n "${KEEP_TEMP:-}" ]]; then
  echo "Keeping temp dir: $TMP_DIR"
else
  rm -rf "$TMP_DIR"
fi

echo "== build-dmg: finished. DMG at $DMG_NAME =="