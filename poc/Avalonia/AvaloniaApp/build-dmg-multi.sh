#!/bin/bash

# This script produces a DMG containing both the x64 and arm64 app bundles
# (no lipo merging). Each bundle is signed separately. The DMG also contains
# a tiny launcher script which auto-selects the appropriate bundle for the
# current machine and a README explaining choices.
#
# Usage:
#  SIGN_ID="Developer ID Application: Your Name (TEAMID)" ./build-dmg-multi.sh

set -euo pipefail

APP_NAME="AvaloniaApp"
BUNDLE_ID="com.sideprompter.app"
VERSION="0.0.1"
DMG_NAME="${APP_NAME}-${VERSION}-multi.dmg"
STAGING_DIR="./bin/dmg-multi-staging"
PUBLISH_DIR="bin/publish"
BACKGROUND_IMAGE="installer_background.jpg"
APP_PROJECT="AvaloniaApp.csproj"
ENTITLEMENTS="AvaloniaApp.entitlements"

X64_OUTPUT="./bin/Release/net9.0-macos/osx-x64"
ARM_OUTPUT="./bin/Release/net9.0-macos/osx-arm64"

cleanup() {
    echo "Cleaning up..."
    if [ -d "${MOUNT_DIR:-/Volumes/${APP_NAME}}" ]; then
        hdiutil detach "${MOUNT_DIR:-/Volumes/${APP_NAME}}" -force 2>/dev/null || true
    fi
    rm -rf "${STAGING_DIR}"
    rm -f "${PUBLISH_DIR}/${DMG_NAME}"
}

# Require SIGN_ID to be provided.
if [ -z "${SIGN_ID:-}" ]; then
    echo "Error: SIGN_ID is not set. Please set SIGN_ID to your code signing identity and re-run."
    echo "Example: SIGN_ID=\"Developer ID Application: Your Name (TEAMID)\" ./build-dmg-multi.sh"
    exit 1
fi

# Only run cleanup on error. Previously we also ran cleanup on EXIT which
# removed the produced DMG even on success. Keep EXIT free so the DMG stays.
trap cleanup ERR

echo "Preparing directories..."
rm -rf "${STAGING_DIR}"
rm -rf "${PUBLISH_DIR}"
mkdir -p "${STAGING_DIR}"
mkdir -p "${PUBLISH_DIR}"

echo "Publishing for x64 and arm64 (Release, self-contained)..."
#dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-x64 -o "${X64_OUTPUT}"
#dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-arm64 -o "${ARM_OUTPUT}"

X64_APP_PATH="${X64_OUTPUT}/${APP_NAME}.app"
ARM_APP_PATH="${ARM_OUTPUT}/${APP_NAME}.app"

if [ ! -d "${X64_APP_PATH}" ] && [ ! -d "${ARM_APP_PATH}" ]; then
    echo "Error: Neither x64 nor arm64 app bundles were found. Please build the app first."
    exit 1
fi

# Helper to copy arch-specific runtimes into the app bundle; do NOT merge
# binaries between archs. Each app bundle keeps only its own arch runtimes.
copy_runtimes_for_arch() {
    app_path="$1"
    arch="$2" # expected: macos-x64 or macos-arm64
    if [ ! -d "${app_path}" ]; then
        return
    fi
    src_arch_dir="${app_path}/Contents/MonoBundle/runtimes/${arch}"
    target_dir="${app_path}/Contents/MonoBundle"
    if [ -d "${src_arch_dir}" ]; then
        echo "Copying ${arch} files from ${src_arch_dir} into ${target_dir} for app ${app_path}..."
        mkdir -p "${target_dir}"
        if command -v rsync >/dev/null 2>&1; then
            rsync -a "${src_arch_dir}/" "${target_dir}/" || cp -R "${src_arch_dir}"/* "${target_dir}/" 2>/dev/null || true
        else
            cp -R "${src_arch_dir}/"* "${target_dir}/" 2>/dev/null || true
        fi
    else
        echo "No ${arch} runtime folder in ${app_path}; skipping."
    fi
}

STAGED_X64_APP="${STAGING_DIR}/${APP_NAME}-x64.app"
STAGED_ARM_APP="${STAGING_DIR}/${APP_NAME}-arm64.app"

if [ -d "${X64_APP_PATH}" ]; then
    echo "Staging x64 app as ${STAGED_X64_APP}"
    cp -R "${X64_APP_PATH}" "${STAGED_X64_APP}"
    # copy arch runtimes into staged bundle (keeps only x64 runtimes)
    copy_runtimes_for_arch "${STAGED_X64_APP}" "macos-x64"
fi

if [ -d "${ARM_APP_PATH}" ]; then
    echo "Staging arm64 app as ${STAGED_ARM_APP}"
    cp -R "${ARM_APP_PATH}" "${STAGED_ARM_APP}"
    copy_runtimes_for_arch "${STAGED_ARM_APP}" "macos-arm64"
fi

# Signer helper (safe: continue on nested failures)
sign_file() {
    fpath="$1"
    echo "Signing: ${fpath}"
    codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${fpath}" || echo "Warning: failed to sign ${fpath}"
}

sign_bundle() {
    bundle_path="$1"
    echo "Signing nested items in ${bundle_path}..."
    # 1) Sign native libs under MonoBundle
    if [ -d "${bundle_path}/Contents/MonoBundle" ]; then
        find "${bundle_path}/Contents/MonoBundle" -type f \( -name '*.dylib' -o -name '*.so' -o -name '*.jnilib' \) -print0 | while IFS= read -r -d '' f; do
            sign_file "$f"
        done
    fi

    # 2) Sign framework dylibs
    if [ -d "${bundle_path}/Contents/Frameworks" ]; then
        find "${bundle_path}/Contents/Frameworks" -type f -name '*.dylib' -print0 | while IFS= read -r -d '' fw; do
            sign_file "$fw"
        done
    fi

    # 3) Sign executables in Contents/MacOS
    if [ -d "${bundle_path}/Contents/MacOS" ]; then
        find "${bundle_path}/Contents/MacOS" -type f -print0 | while IFS= read -r -d '' exe; do
            if [ -f "$exe" ]; then
                sign_file "$exe"
            fi
        done
    fi

    # 4) Final top-level sign (use entitlements if available)
    if [ -f "${ENTITLEMENTS}" ]; then
        echo "Signing ${bundle_path} with entitlements file ${ENTITLEMENTS}"
        codesign --sign "${SIGN_ID}" --options runtime --entitlements "${ENTITLEMENTS}" --timestamp --force "${bundle_path}" || echo "Warning: failed to sign ${bundle_path}"
    else
        echo "Signing ${bundle_path} without entitlements"
        codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${bundle_path}" || echo "Warning: failed to sign ${bundle_path}"
    fi

    echo "Verification for ${bundle_path}:"
    codesign -dv --verbose=4 "${bundle_path}" || true
}

if [ -d "${STAGED_X64_APP}" ]; then
    sign_bundle "${STAGED_X64_APP}"
fi
if [ -d "${STAGED_ARM_APP}" ]; then
    sign_bundle "${STAGED_ARM_APP}"
fi

# Create a small launcher script that auto-selects the right bundle inside the DMG
LAUNCHER="${STAGING_DIR}/Launch ${APP_NAME}.command"
cat > "${LAUNCHER}" <<'LAUNCH_SCRIPT'
#!/bin/bash
# Auto-launcher: detects host architecture and opens the matching app bundle
HERE="$(dirname "$0")"
OS_ARCH="$(uname -m)"
if [ "$OS_ARCH" = "arm64" ]; then
    TARGET_APP="$HERE/AvaloniaApp-arm64.app"
else
    TARGET_APP="$HERE/AvaloniaApp-x64.app"
fi
if [ -d "$TARGET_APP" ]; then
    open "$TARGET_APP"
else
    # Fallback: ask user which to open
    /usr/bin/osascript -e 'set t to {"AvaloniaApp (x64)", "AvaloniaApp (arm64)"}' -e 'choose from list t with prompt "Could not determine correct app; choose which to open:" default items {item 1 of t}' >/dev/null 2>&1
    # If user selected, try to open the one they picked (handled via dialog above)
fi
LAUNCH_SCRIPT
chmod +x "${LAUNCHER}"

# Create a README with guidance for users (also helps when DMG is opened)
cat > "${STAGING_DIR}/README.txt" <<README
This disk image contains two versions of ${APP_NAME}:

- ${APP_NAME}-x64.app  (for Intel macs)
- ${APP_NAME}-arm64.app (for Apple Silicon macs)

If you double-click "Launch ${APP_NAME}.command" the DMG will attempt to
open the correct build automatically for your Mac.

To install, drag the appropriate .app to the Applications folder.
On modern macs you should use the arm64 build; on Intel-based Macs use the x64 build.
If unsure, use the launcher to auto-select.
README

# Copy background image into staging if present
if [ -f "${BACKGROUND_IMAGE}" ]; then
    mkdir -p "${STAGING_DIR}/.background"
    cp "${BACKGROUND_IMAGE}" "${STAGING_DIR}/.background/background.jpg"
fi

if ! command -v create-dmg >/dev/null 2>&1; then
    if command -v brew >/dev/null 2>&1; then
        echo "Installing create-dmg via Homebrew..."
        brew install create-dmg
    else
        echo "Error: create-dmg not found and Homebrew is not available. Please install Homebrew and run: brew install create-dmg"
        exit 1
    fi
fi

echo "Creating DMG with both app bundles and launcher..."
CREATE_DMG_CMD=(create-dmg
  --volname "${APP_NAME}"
)
if [ -f "${STAGING_DIR}/.background/background.jpg" ]; then
  CREATE_DMG_CMD+=(--background "${STAGING_DIR}/.background/background.jpg")
fi
CREATE_DMG_CMD+=(
  --window-pos 200 120
  --window-size 800 400
  --icon-size 100
  --icon "${APP_NAME}-x64.app" 180 190
  --icon "${APP_NAME}-arm64.app" 380 190
  --icon "Launch ${APP_NAME}.command" 280 300
  --hide-extension "${APP_NAME}-x64.app"
  --hide-extension "${APP_NAME}-arm64.app"
  --app-drop-link 600 185
  "${PUBLISH_DIR}/${DMG_NAME}"
  "${STAGING_DIR}"
)

"${CREATE_DMG_CMD[@]}"

if [ -f "${PUBLISH_DIR}/${DMG_NAME}" ]; then
    echo "Successfully created ${DMG_NAME} at ${PUBLISH_DIR}"
else
    echo "create-dmg failed to produce a DMG in ${PUBLISH_DIR}"
    exit 1
fi

# final cleanup of staging
rm -rf "${STAGING_DIR}"

echo "Done."
