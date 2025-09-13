#!/bin/bash

# This script builds a universal "wrapper" app that contains both x64 and arm64
# bundles, and a launcher that automatically runs the correct one.
#
# Usage:
#  SIGN_ID="Developer ID Application: Your Name (TEAMID)" ./build-dmg-wrapper.sh

set -euo pipefail

# --- Configuration ---
APP_NAME="SidePrompter"
BUNDLE_ID="com.sideprompter.app"
VERSION="0.0.1"
DMG_NAME="${APP_NAME}-${VERSION}-universal.dmg"
STAGING_DIR="./bin/dmg-wrapper-staging"
PUBLISH_DIR="bin/publish"
BACKGROUND_IMAGE="installer_background.jpg"
APP_PROJECT="AvaloniaApp.csproj"
ENTITLEMENTS="SidePrompter.entitlements"

# Paths to pre-built x64 and arm64 app bundles
X64_OUTPUT="./bin/Release/net9.0-macos/osx-x64"
ARM_OUTPUT="./bin/Release/net9.0-macos/osx-arm64"
# --- End Configuration ---

X64_APP_PATH="${X64_OUTPUT}/${APP_NAME}.app"
ARM_APP_PATH="${ARM_OUTPUT}/${APP_NAME}.app"
WRAPPER_APP_PATH="${STAGING_DIR}/${APP_NAME}.app"

cleanup() {
    echo "Cleaning up..."
    if [ -d "${MOUNT_DIR:-/Volumes/${APP_NAME}}" ]; then
        hdiutil detach "${MOUNT_DIR:-/Volumes/${APP_NAME}}" -force 2>/dev/null || true
    fi
    rm -rf "${STAGING_DIR}"
    rm -f "${PUBLISH_DIR}/${DMG_NAME}"
}

# Require SIGN_ID
if [ -z "${SIGN_ID:-}" ]; then
    echo "Error: SIGN_ID is not set. Please set it and re-run."
    echo "Example: SIGN_ID=\"Developer ID Application: Your Name (TEAMID)\" ./build-dmg-wrapper.sh"
    exit 1
fi

trap cleanup ERR

# --- Preparation ---
echo "Preparing directories..."
rm -rf "${STAGING_DIR}"
rm -rf "${PUBLISH_DIR}"
mkdir -p "${STAGING_DIR}"
mkdir -p "${PUBLISH_DIR}"

if [ ! -d "${X64_APP_PATH}" ] || [ ! -d "${ARM_APP_PATH}" ]; then
    echo "Error: x64 and/or arm64 app bundles not found. Please build them first."
    exit 1
fi

# --- Build Wrapper App ---
echo "Creating wrapper app skeleton at ${WRAPPER_APP_PATH}"
mkdir -p "${WRAPPER_APP_PATH}/Contents/MacOS"
mkdir -p "${WRAPPER_APP_PATH}/Contents/Resources"

echo "Creating Info.plist for wrapper app..."
# Use the arm64 Info.plist as a template
cp "${ARM_APP_PATH}/Contents/Info.plist" "${WRAPPER_APP_PATH}/Contents/Info.plist"
# Set the executable to our launcher script
plutil -replace CFBundleExecutable -string "launcher" "${WRAPPER_APP_PATH}/Contents/Info.plist"
# Copy icon file if it exists
if [ -f "${ARM_APP_PATH}/Contents/Resources/icon.icns" ]; then
    cp "${ARM_APP_PATH}/Contents/Resources/icon.icns" "${WRAPPER_APP_PATH}/Contents/Resources/icon.icns"
fi

# Prepare DMG background if provided
echo "Setting up DMG background..."
if [ -n "${BACKGROUND_IMAGE:-}" ] && [ -f "${BACKGROUND_IMAGE}" ]; then
    echo "Including background image ${BACKGROUND_IMAGE} in DMG staging"
    mkdir -p "${STAGING_DIR}/.background"
    # copy and normalize name to a common filename used by create-dmg
    cp "${BACKGROUND_IMAGE}" "${STAGING_DIR}/.background/background.jpg"
else
    echo "No background image found at ${BACKGROUND_IMAGE:-'(none)'}; DMG will use default background"
fi

echo "Creating launcher script..."
LAUNCHER_SCRIPT_PATH="${WRAPPER_APP_PATH}/Contents/MacOS/launcher"
cat > "${LAUNCHER_SCRIPT_PATH}" << LAUNCHER
#!/bin/bash
HERE=\$(dirname "\$0")
APP_NAME="${APP_NAME}"
OS_ARCH=\$(uname -m)

if [ "\$OS_ARCH" = "arm64" ]; then
    REAL_EXECUTABLE="\$HERE/../Resources/${APP_NAME}-arm64.app/Contents/MacOS/${APP_NAME}"
else
    REAL_EXECUTABLE="\$HERE/../Resources/${APP_NAME}-x64.app/Contents/MacOS/${APP_NAME}"
fi

# Execute the real binary, passing along all arguments
exec "\$REAL_EXECUTABLE" "\$@"
LAUNCHER
chmod +x "${LAUNCHER_SCRIPT_PATH}"

echo "Copying inner app bundles into wrapper..."
cp -R "${X64_APP_PATH}" "${WRAPPER_APP_PATH}/Contents/Resources/${APP_NAME}-x64.app"
cp -R "${ARM_APP_PATH}" "${WRAPPER_APP_PATH}/Contents/Resources/${APP_NAME}-arm64.app"

# --- Signing ---
sign_file() {
    fpath="$1"
    echo "Signing: ${fpath}"
    codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${fpath}" || echo "Warning: failed to sign ${fpath}"
}

sign_bundle() {
    bundle_path="$1"
    echo "Signing nested items in ${bundle_path}..."
    if [ -d "${bundle_path}/Contents/MonoBundle" ]; then
        find "${bundle_path}/Contents/MonoBundle" -type f \( -name '*.dylib' -o -name '*.so' -o -name '*.jnilib' \) -print0 | while IFS= read -r -d '' f; do
            sign_file "$f"
        done
    fi
    if [ -d "${bundle_path}/Contents/Frameworks" ]; then
        find "${bundle_path}/Contents/Frameworks" -type f -name '*.dylib' -print0 | while IFS= read -r -d '' fw; do
            sign_file "$fw"
        done
    fi
    if [ -d "${bundle_path}/Contents/MacOS" ]; then
        find "${bundle_path}/Contents/MacOS" -type f -print0 | while IFS= read -r -d '' exe; do
            if [ -f "$exe" ]; then
                sign_file "$exe"
            fi
        done
    fi
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

echo "Signing inner bundles..."
sign_bundle "${WRAPPER_APP_PATH}/Contents/Resources/${APP_NAME}-x64.app"
sign_bundle "${WRAPPER_APP_PATH}/Contents/Resources/${APP_NAME}-arm64.app"

echo "Signing wrapper app..."
# Sign the launcher script itself
sign_file "${LAUNCHER_SCRIPT_PATH}"

# Sign the final wrapper bundle. We don't use --deep because we've signed contents manually.
if [ -f "${ENTITLEMENTS}" ]; then
    echo "Signing wrapper with entitlements..."
    codesign --sign "${SIGN_ID}" --options runtime --entitlements "${ENTITLEMENTS}" --timestamp --force "${WRAPPER_APP_PATH}"
else
    echo "Signing wrapper without entitlements..."
    codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${WRAPPER_APP_PATH}"
fi

echo "Verifying final wrapper app..."
codesign -dv --verbose=4 "${WRAPPER_APP_PATH}"

echo "Checking Gatekeeper acceptance... (Note: this may be 'rejected' with development certificates)"
spctl -a -vv "${WRAPPER_APP_PATH}" || true

# --- Create DMG ---
echo "Creating final DMG..."
if [ -f "${STAGING_DIR}/.background/background.jpg" ]; then
    create-dmg \
        --volname "${APP_NAME}" \
        --window-pos 200 120 \
        --window-size 600 350 \
        --icon-size 128 \
        --app-drop-link 425 150 \
        --icon "${APP_NAME}.app" 175 150 \
        --background "${STAGING_DIR}/.background/background.jpg" \
        "${PUBLISH_DIR}/${DMG_NAME}" \
        "${STAGING_DIR}"
else
    create-dmg \
        --volname "${APP_NAME}" \
        --window-pos 200 120 \
        --window-size 600 350 \
        --icon-size 128 \
        --app-drop-link 425 150 \
        --icon "${APP_NAME}.app" 175 150 \
        "${PUBLISH_DIR}/${DMG_NAME}" \
        "${STAGING_DIR}"
fi

# --- Final Cleanup ---
rm -rf "${STAGING_DIR}"

echo "Done. Universal wrapper DMG created at ${PUBLISH_DIR}/${DMG_NAME}"
