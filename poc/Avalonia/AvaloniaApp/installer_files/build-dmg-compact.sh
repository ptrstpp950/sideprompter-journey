#!/bin/bash

# This script builds a compact, universal "wrapper" app. It reduces size by
# sharing common .dll files between the x64 and arm64 bundles using symlinks.
#
# Usage:
#  SIGN_ID="Developer ID Application: Your Name (TEAMID)" ./build-dmg-compact.sh

set -euo pipefail

# --- Configuration ---
APP_NAME="SidePrompter"
BUNDLE_ID="com.sideprompter.app"
VERSION="0.0.1"
DMG_NAME="${APP_NAME}-${VERSION}-compact.dmg"
STAGING_DIR="./bin/dmg-compact-staging"
PUBLISH_DIR="bin/publish"
BACKGROUND_IMAGE="installer_files/installer_background.jpg"
BACKGROUND_IMAGE_DS_STORE="installer_files/installer-DS_Store" # Example of ignoring a file
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
    exit 1
fi

trap cleanup ERR

# --- Preparation ---
echo "Preparing directories..."
rm -rf "${STAGING_DIR}"
rm -rf "${PUBLISH_DIR}"
mkdir -p "${STAGING_DIR}"
mkdir -p "${PUBLISH_DIR}"

# The script assumes the apps have been published.
dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-x64 -o "${X64_OUTPUT}"
dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-arm64 -o "${ARM_OUTPUT}"


if [ ! -d "${X64_APP_PATH}" ] || [ ! -d "${ARM_APP_PATH}" ]; then
    echo "Error: x64 and/or arm64 app bundles not found. Please build them first."
    exit 1
fi

# --- Build Wrapper App ---
echo "Creating wrapper app skeleton..."
SHARED_DIR="${WRAPPER_APP_PATH}/Contents/Shared"
mkdir -p "${WRAPPER_APP_PATH}/Contents/MacOS"
mkdir -p "${WRAPPER_APP_PATH}/Contents/Resources"
mkdir -p "${SHARED_DIR}"

echo "Creating Info.plist and launcher script..."
cp "${ARM_APP_PATH}/Contents/Info.plist" "${WRAPPER_APP_PATH}/Contents/Info.plist"
plutil -replace CFBundleExecutable -string "launcher" "${WRAPPER_APP_PATH}/Contents/Info.plist"
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
exec "\$REAL_EXECUTABLE" "\$@"
LAUNCHER
chmod +x "${LAUNCHER_SCRIPT_PATH}"

# --- DLL Sharing ---
echo "Copying full app bundles to temporary location..."
ARM_RESOURCES_APP_PATH="${WRAPPER_APP_PATH}/Contents/Resources/${APP_NAME}-arm64.app"
X64_RESOURCES_APP_PATH="${WRAPPER_APP_PATH}/Contents/Resources/${APP_NAME}-x64.app"
cp -R "${ARM_APP_PATH}" "${ARM_RESOURCES_APP_PATH}"
cp -R "${X64_APP_PATH}" "${X64_RESOURCES_APP_PATH}"

ARM_MONO_BUNDLE_PATH="${ARM_RESOURCES_APP_PATH}/Contents/MonoBundle"
X64_MONO_BUNDLE_PATH="${X64_RESOURCES_APP_PATH}/Contents/MonoBundle"

echo "Processing DLLs for sharing..."
# Find all DLLs in the arm64 bundle to decide whether to share them
find "${ARM_MONO_BUNDLE_PATH}" -name '*.dll' -print0 | while IFS= read -r -d '' arm_dll_path; do
    dll_name=$(basename "$arm_dll_path")
    x64_dll_path="${X64_MONO_BUNDLE_PATH}/${dll_name}"

    # Check if the corresponding x64 DLL exists
    if [ ! -f "${x64_dll_path}" ]; then
        echo "Info: ${dll_name} only exists in arm64 bundle. Skipping."
        continue
    fi

    # Compare the two DLLs
    if cmp -s "$arm_dll_path" "$x64_dll_path"; then
        # They are identical, so we can share them.
        echo "Sharing identical DLL: ${dll_name}"
        
        # Move the arm64 version to the shared folder
        mv "$arm_dll_path" "${SHARED_DIR}/"
        
        # Delete the x64 version
        rm "$x64_dll_path"
        
        # Create symlinks in both bundles
        (cd "${ARM_MONO_BUNDLE_PATH}" && ln -s "../../../../Shared/${dll_name}" "${dll_name}")
        (cd "${X64_MONO_BUNDLE_PATH}" && ln -s "../../../../Shared/${dll_name}" "${dll_name}")
    else
        # They are different, so we keep both.
        echo "Info: DLL mismatch for ${dll_name}. Keeping separate copies."
    fi
done

# --- Signing ---
sign_file() {
    fpath="$1"
    echo "Signing: ${fpath}"
    codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${fpath}" || echo "Warning: failed to sign ${fpath}"
}
sign_bundle() {
    bundle_path="$1"
    echo "Signing nested items in ${bundle_path}..."
    # This function is simplified as we assume a standard .NET bundle structure
    # It signs all found native libraries and the main executable
    find "${bundle_path}" -type f '(' -name '*.dylib' -o -name '*.so' -o -name '*.jnilib' ')' -print0 | while IFS= read -r -d '' f; do
        sign_file "$f"
    done
    find "${bundle_path}/Contents/MacOS" -type f -print0 | while IFS= read -r -d '' exe; do
        if [ -f "$exe" ]; then sign_file "$exe"; fi
    done
    if [ -f "${ENTITLEMENTS}" ]; then
        codesign --sign "${SIGN_ID}" --options runtime --entitlements "${ENTITLEMENTS}" --timestamp --force "${bundle_path}"
    else
        codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${bundle_path}"
    fi
}

echo "Signing inner bundles (with symlinks)"...
sign_bundle "${ARM_RESOURCES_APP_PATH}"
sign_bundle "${X64_RESOURCES_APP_PATH}"

echo "Signing wrapper app"...
sign_file "${LAUNCHER_SCRIPT_PATH}"
if [ -f "${ENTITLEMENTS}" ]; then
    codesign --sign "${SIGN_ID}" --options runtime --entitlements "${ENTITLEMENTS}" --timestamp --force "${WRAPPER_APP_PATH}"
else
    codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${WRAPPER_APP_PATH}"
fi

echo "Verifying final wrapper app"...
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
echo "DMG created at ${PUBLISH_DIR}/${DMG_NAME}"

# --- Final Cleanup ---
rm -rf "${STAGING_DIR}"
echo "Done. Compact wrapper DMG created at ${PUBLISH_DIR}/${DMG_NAME}"