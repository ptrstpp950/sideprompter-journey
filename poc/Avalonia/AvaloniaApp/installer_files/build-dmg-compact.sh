#!/bin/bash

# This script builds a compact, universal "wrapper" app. It reduces size by
# sharing common .dll files between the x64 and arm64 bundles using symlinks.
#
# Usage:
#  SIGN_ID="Developer ID Application: Your Name (TEAMID)" ./build-dmg-compact.sh [--no-publish|--skip-publish] [--publish] [-h|--help]
#
# Options:
#  --no-publish, --skip-publish   Skip running `dotnet publish` (use existing builds)
#  --publish                      Force running `dotnet publish` (default behavior)
#  -h, --help                     Show this help and exit

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

# Control whether this script runs `dotnet publish` (default: run).
# Set RUN_PUBLISH=0 or RUN_PUBLISH=false to skip publishing when you already
# have prebuilt app bundles in the expected output folders.
RUN_PUBLISH="${RUN_PUBLISH:-1}"

# Parse command-line arguments to allow overriding RUN_PUBLISH.
print_help() {
    sed -n '1,120p' "$0" | sed -n '1,40p' >/dev/stderr || true
    cat <<'HELP'

Examples:
  # Run and publish (default)
  SIGN_ID="..." ./build-dmg-compact.sh

  # Skip publishing and use prebuilt app bundles
  SIGN_ID="..." ./build-dmg-compact.sh --no-publish

HELP
}

for arg in "$@"; do
    case "$arg" in
        --no-publish|--skip-publish)
            RUN_PUBLISH=0
            ;;
        --publish)
            RUN_PUBLISH=1
            ;;
        -h|--help)
            print_help
            exit 0
            ;;
        *)
            # Unknown args are ignored so existing usage still works
            ;;
    esac
done

# Credentials for create-dmg notarization. THIS IS REQUIRED — the script
# will exit if this variable is not set. create-dmg will be called with
# --notarize "${NOTARIZE_CREDENTIALS}" which submits the resulting DMG to
# Apple's notarization service, waits for completion, and staples the result.
# Example values:
#  - "you@apple.com:APP_SPECIFIC_PASSWORD"
#  - "@keychain:AC_PASSWORD" (if you store the password in keychain)
NOTARIZE_CREDENTIALS="${NOTARIZE_CREDENTIALS:-}"

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

# Optionally build and sign helper/native libraries located in `libs/`.
# Set SIGN_LIBS=0 to skip this step (default: 1).
SIGN_LIBS="${SIGN_LIBS:-1}"

if [ "${SIGN_LIBS}" != "0" ]; then
    echo "Building and signing helper binaries in libs/ (activeWindowTextGetter, hotkeyListener, audiotee)..."

    sign_local_file() {
        fpath="$1"
        if [ -f "${fpath}" ]; then
            echo "Signing local file: ${fpath}"
            chmod +x "${fpath}" || true
            codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${fpath}" || echo "Warning: failed to sign ${fpath}"
        else
            echo "Info: file not found, skipping: ${fpath}"
        fi
    }

    # Build and sign Swift executable targets (if Swift is available)
    for pkg in activeWindowTextGetter hotkeyListener; do
        pkgdir="libs/${pkg}"
        if [ -d "${pkgdir}" ]; then
            echo "Building Swift package: ${pkgdir}"
            (cd "${pkgdir}" && swift build -c release) || echo "Warning: swift build failed for ${pkgdir}"
            binpath="${pkgdir}/.build/release/${pkg}"
            sign_local_file "${binpath}"
        else
            echo "Info: package directory not found, skipping: ${pkgdir}"
        fi
    done

    # Sign audiotee script/binary if present
    AUDIO_TEE_BIN="libs/audiotee/bin/audiotee"
    sign_local_file "${AUDIO_TEE_BIN}"
fi

# Notarization credentials are required for distribution. Fail early if missing.
if [ -z "${NOTARIZE_CREDENTIALS}" ]; then
        echo "Error: NOTARIZE_CREDENTIALS is not set. This script requires create-dmg notarization credentials to be set in NOTARIZE_CREDENTIALS."
        echo "Example: export NOTARIZE_CREDENTIALS=\"you@apple.com:APP_SPECIFIC_PASSWORD\""
        echo "If you want to store credentials with Apple's notarytool, you can run the following one-time command to store them in your keychain:"
        cat <<'CMD'
xcrun notarytool store-credentials \
    --apple-id "TODO_YOUR_APPLE_ID" \
    --password "TODO_ONE_TIME_PASSWORD_FROM_APPLE" \
    --team-id "TODO_YOUR_TEAM_ID" \
    appleid-notarytool

# After storing, set NOTARIZE_CREDENTIALS to reference the keychain entry:
# export NOTARIZE_CREDENTIALS="@keychain:appleid-notarytool"
CMD
        exit 1
fi

# The script assumes the apps have been published. By default this script will
# call `dotnet publish` for x64 and arm64. To skip publishing (e.g. when you
# already have builds in ${PUBLISH_DIR}), set RUN_PUBLISH=0 or RUN_PUBLISH=false
# in the environment before running the script.
if [ "${RUN_PUBLISH}" = "0" ] || [ "${RUN_PUBLISH}" = "false" ]; then
    echo "Skipping dotnet publish because RUN_PUBLISH=${RUN_PUBLISH}"
else
    echo "Publishing x64 and arm64 builds to ${X64_OUTPUT} and ${ARM_OUTPUT}..."
    dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-x64 -o "${X64_OUTPUT}"
    dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-arm64 -o "${ARM_OUTPUT}"
fi


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
    # Ensure executable bit when signing executables/scripts
    if [ -f "${fpath}" ]; then
        chmod +x "${fpath}" || true
    fi
    codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${fpath}" || echo "Warning: failed to sign ${fpath}"
    # Verify signature (non-fatal)
    if [ -f "${fpath}" ]; then
        codesign --verify --verbose=4 "${fpath}" 2>/dev/null || echo "Warning: verification failed for ${fpath}"
    fi
}

sign_bundle() {
    bundle_path="$1"
    echo "Signing nested items in ${bundle_path}..."

    # Sign common native libraries
    find "${bundle_path}" -type f \( -name '*.dylib' -o -name '*.so' -o -name '*.jnilib' \) -print0 | while IFS= read -r -d '' f; do
        sign_file "$f"
    done

    # Sign executables in Contents/MacOS
    if [ -d "${bundle_path}/Contents/MacOS" ]; then
        find "${bundle_path}/Contents/MacOS" -type f -print0 | while IFS= read -r -d '' exe; do
            [ -f "$exe" ] && sign_file "$exe"
        done
    fi

    # Sign any executable files anywhere under Resources (helps catch helper binaries)
    if [ -d "${bundle_path}/Contents/Resources" ]; then
        find "${bundle_path}/Contents/Resources" -type f -perm -111 -print0 | while IFS= read -r -d '' execf; do
            echo "Found executable in Resources: ${execf}"
            sign_file "${execf}"
        done

        # Additionally sign known helper binary names even if execute bit isn't set yet
        find "${bundle_path}/Contents/Resources" -type f \( -name 'activeWindowTextGetter' -o -name 'hotkeyListener' -o -name 'audiotee' \) -print0 | while IFS= read -r -d '' helper; do
            echo "Found helper binary: ${helper}"
            chmod +x "${helper}" || true
            sign_file "${helper}"
        done
    fi

    # Finally sign the bundle itself (with entitlements if provided)
    if [ -f "${ENTITLEMENTS}" ]; then
        codesign --sign "${SIGN_ID}" --options runtime --entitlements "${ENTITLEMENTS}" --timestamp --force "${bundle_path}" || echo "Warning: failed to sign bundle ${bundle_path}"
    else
        codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${bundle_path}" || echo "Warning: failed to sign bundle ${bundle_path}"
    fi

    # Verify bundle signature (non-fatal)
    codesign --verify --deep --verbose=4 "${bundle_path}" 2>/dev/null || echo "Warning: bundle verification failed for ${bundle_path}"
}

echo "Signing inner bundles (with symlinks)"
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
# Build create-dmg arguments and optionally include notarization credentials
NOTARIZE_ARG=()
if [ -n "${NOTARIZE_CREDENTIALS}" ]; then
    echo "Will request notarization via create-dmg using provided credentials"
    NOTARIZE_ARG=(--notarize "${NOTARIZE_CREDENTIALS}")
fi

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
        #"${NOTARIZE_ARG[@]}" \
        
else
    create-dmg \
        --volname "${APP_NAME}" \
        --window-pos 200 120 \
        --window-size 600 350 \
        --icon-size 128 \
        --app-drop-link 425 150 \
        --icon "${APP_NAME}.app" 175 150 \
        "${NOTARIZE_ARG[@]}" \
        "${PUBLISH_DIR}/${DMG_NAME}" \
        "${STAGING_DIR}"
fi
echo "DMG created at ${PUBLISH_DIR}/${DMG_NAME}"

# --- Final Cleanup ---
rm -rf "${STAGING_DIR}"
echo "Done. Compact wrapper DMG created at ${PUBLISH_DIR}/${DMG_NAME}"