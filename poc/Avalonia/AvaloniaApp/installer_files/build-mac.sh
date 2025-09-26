#!/bin/bash

# Create a universal "wrapper" app and sign it. Stops before DMG creation.
# Usage:
#  SIGN_ID="Developer ID Application: Your Name (TEAMID)" ./create-wrapper-app.sh [--no-publish|--skip-publish] [--publish]

set -euo pipefail

# --- Configuration ---
APP_NAME="SidePrompter"
APP_PROJECT="AvaloniaApp.csproj"
STAGING_DIR="./bin/wrapper-staging"
PUBLISH_X64="./bin/Release/net9.0-macos/osx-x64"
PUBLISH_ARM="./bin/Release/net9.0-macos/osx-arm64"
ENTITLEMENTS="SidePrompter.entitlements"
RUN_PUBLISH="${RUN_PUBLISH:-0}"  # Default to not publishing
VERSION="${VERSION:-0.0.1}"

print_help() {
    sed -n '1,200p' "$0" | sed -n '1,40p' >/dev/stderr || true
    cat <<'HELP'

Examples:
  # Run and publish (default)
    SIGN_ID="..." ./installer_files/create-wrapper-app.sh

    # Specify a Velopack version
    SIGN_ID="..." VERSION=1.2.3 ./installer_files/create-wrapper-app.sh

  # Skip publishing and use prebuilt app bundles
  SIGN_ID="..." ./installer_files/create-wrapper-app.sh --no-publish

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
            --version=*)
                VERSION="${arg#*=}"
                ;;
            -v=*)
                VERSION="${arg#*=}"
                ;;
        *)
            ;;
    esac
done

# Require SIGN_ID
if [ -z "${SIGN_ID:-}" ]; then
    echo "Error: SIGN_ID is not set. Please set it and re-run."
    exit 1
fi

cleanup() {
    echo "(info) cleanup: leaving staging dir for inspection: ${STAGING_DIR}"
}
trap cleanup EXIT

# Optionally publish
if [ "${RUN_PUBLISH}" = "0" ] || [ "${RUN_PUBLISH}" = "false" ]; then
    echo "Skipping dotnet publish because RUN_PUBLISH=${RUN_PUBLISH}"
else
    echo "Publishing x64 and arm64 builds to ${PUBLISH_X64} and ${PUBLISH_ARM}..."
    dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-x64 -o "${PUBLISH_X64}"
    dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-arm64 -o "${PUBLISH_ARM}"
fi

X64_APP_PATH="${PUBLISH_X64}/${APP_NAME}.app"
ARM_APP_PATH="${PUBLISH_ARM}/${APP_NAME}.app"
WRAPPER_APP_PATH="${STAGING_DIR}/${APP_NAME}.app"

if [ ! -d "${X64_APP_PATH}" ] || [ ! -d "${ARM_APP_PATH}" ]; then
    echo "Error: x64 and/or arm64 app bundles not found. Please build them first or run without --no-publish."
    exit 1
fi

# Create wrapper skeleton
echo "Creating wrapper app at ${WRAPPER_APP_PATH}..."
rm -rf "${STAGING_DIR}"
mkdir -p "${WRAPPER_APP_PATH}/Contents/MacOS"
mkdir -p "${WRAPPER_APP_PATH}/Contents/Resources"

# Copy Info.plist and adjust launcher executable name
cp "${ARM_APP_PATH}/Contents/Info.plist" "${WRAPPER_APP_PATH}/Contents/Info.plist"
plutil -replace CFBundleExecutable -string "launcher" "${WRAPPER_APP_PATH}/Contents/Info.plist"

# Copy icon if present
if [ -f "${ARM_APP_PATH}/Contents/Resources/icon.icns" ]; then
    cp "${ARM_APP_PATH}/Contents/Resources/icon.icns" "${WRAPPER_APP_PATH}/Contents/Resources/icon.icns"
fi

# Create launcher script
LAUNCHER_SCRIPT_PATH="${WRAPPER_APP_PATH}/Contents/MacOS/launcher"
cat > "${LAUNCHER_SCRIPT_PATH}" <<'LAUNCHER'
#!/bin/bash
HERE=$(dirname "$0")
APP_NAME="SidePrompter"
OS_ARCH=$(uname -m)
if [ "$OS_ARCH" = "arm64" ]; then
    REAL_EXECUTABLE="$HERE/../Resources/${APP_NAME}-arm64.app/Contents/MacOS/${APP_NAME}"
else
    REAL_EXECUTABLE="$HERE/../Resources/${APP_NAME}-x64.app/Contents/MacOS/${APP_NAME}"
fi
exec "$REAL_EXECUTABLE" "$@"
LAUNCHER
chmod +x "${LAUNCHER_SCRIPT_PATH}"

# Copy full app bundles into Resources
ARM_RESOURCES_APP_PATH="${WRAPPER_APP_PATH}/Contents/Resources/${APP_NAME}-arm64.app"
X64_RESOURCES_APP_PATH="${WRAPPER_APP_PATH}/Contents/Resources/${APP_NAME}-x64.app"
cp -R "${ARM_APP_PATH}" "${ARM_RESOURCES_APP_PATH}"
cp -R "${X64_APP_PATH}" "${X64_RESOURCES_APP_PATH}"


# Signing helpers
sign_file() {
    fpath="$1"
    echo "Signing: ${fpath}"
    if [ -f "${fpath}" ]; then
        chmod +x "${fpath}" || true
    fi
    codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${fpath}" || echo "Warning: failed to sign ${fpath}"
}

sign_bundle() {
    bundle_path="$1"
    echo "Signing nested items in ${bundle_path}..."

    find "${bundle_path}" -type f \( -name '*.dylib' -o -name '*.so' -o -name '*.jnilib' \) -print0 | while IFS= read -r -d '' f; do
        sign_file "$f"
    done

    if [ -d "${bundle_path}/Contents/MacOS" ]; then
        find "${bundle_path}/Contents/MacOS" -type f -print0 | while IFS= read -r -d '' exe; do
            [ -f "$exe" ] && sign_file "$exe"
        done
    fi

    if [ -d "${bundle_path}/Contents/Resources" ]; then
        find "${bundle_path}/Contents/Resources" -type f -perm -111 -print0 | while IFS= read -r -d '' execf; do
            sign_file "${execf}"
        done

        find "${bundle_path}/Contents/Resources" -type f \( -name 'activeWindowTextGetter' -o -name 'hotkeyListener' -o -name 'audiotee' \) -print0 | while IFS= read -r -d '' helper; do
            chmod +x "${helper}" || true
            sign_file "${helper}"
        done
    fi

    if [ -f "${ENTITLEMENTS}" ]; then
        codesign --sign "${SIGN_ID}" --options runtime --entitlements "${ENTITLEMENTS}" --timestamp --force "${bundle_path}" || echo "Warning: failed to sign bundle ${bundle_path}"
    else
        codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${bundle_path}" || echo "Warning: failed to sign bundle ${bundle_path}"
    fi

    codesign --verify --deep --verbose=4 "${bundle_path}" 2>/dev/null || echo "Warning: bundle verification failed for ${bundle_path}"
}

# Sign inner bundles
echo "Signing inner resource bundles..."
sign_bundle "${ARM_RESOURCES_APP_PATH}"
sign_bundle "${X64_RESOURCES_APP_PATH}"

# Sign launcher and wrapper
echo "Signing launcher script and wrapper app..."
sign_file "${LAUNCHER_SCRIPT_PATH}"

if [ -f "${ENTITLEMENTS}" ]; then
    codesign --sign "${SIGN_ID}" --options runtime --entitlements "${ENTITLEMENTS}" --timestamp --force "${WRAPPER_APP_PATH}" || echo "Warning: failed to sign wrapper ${WRAPPER_APP_PATH}"
else
    codesign --sign "${SIGN_ID}" --options runtime --timestamp --force "${WRAPPER_APP_PATH}" || echo "Warning: failed to sign wrapper ${WRAPPER_APP_PATH}"
fi

# Verification
echo "Final verification:"
codesign -dv --verbose=4 "${WRAPPER_APP_PATH}" || true
spctl -a -vv "${WRAPPER_APP_PATH}" || true

echo "Wrapper app created and signed at: ${WRAPPER_APP_PATH}"
echo "Stopped before DMG creation as requested."

echo "Run vpk to create a Velopack:"
echo "vpk pack -o ./bin/velopack -c osx -u sideprompter -v ${VERSION} -p ${WRAPPER_APP_PATH} --packTitle \"SidePrompter\" --icon Assets/icon.icns --bundleId com.sideprompter.app --signEntitlements ${ENTITLEMENTS} --signAppIdentity \"\$SIGN_ID\" --notaryProfile \"\$NOTARIZE_CREDENTIALS\" --mainExe launcher"

# Run vpk if available
if command -v vpk >/dev/null 2>&1; then
    echo "vpk tool found, creating Velopack with version ${VERSION}..."
    mkdir -p ./bin/velopack
    vpk pack \
        -o ./bin/velopack \
        -c osx \
        -u sideprompter \
        -v "${VERSION}" \
        -p "${WRAPPER_APP_PATH}" \
        --packTitle "SidePrompter" \
        --icon Assets/icon.icns \
        --bundleId com.sideprompter.app \
        --signEntitlements "${ENTITLEMENTS}" \
        --signAppIdentity "${SIGN_ID}" \
        --notaryProfile "${NOTARIZE_CREDENTIALS:-}" \
        --mainExe launcher || echo "Warning: vpk pack failed"
else
    echo "vpk not found in PATH; skipping Velopack creation. Install 'vpk' or run the printed command manually."
fi

# If vpk produced a portable zip, unzip it into the staging dir and create a DMG
VPK_PORTABLE_ZIP="./bin/velopack/sideprompter-osx-Portable.zip"
PUBLISH_DIR="./bin/velopack"
DMG_NAME="${APP_NAME}-${VERSION}.dmg"
STAGING_DMG_DIR="./bin/dmg-staging"
BACKGROUND_IMAGE="installer_files/installer_background.jpg"


mkdir -p "${PUBLISH_DIR}"

if [ -f "${VPK_PORTABLE_ZIP}" ]; then
    echo "Unzipping Velopack portable zip: ${VPK_PORTABLE_ZIP} -> ${STAGING_DMG_DIR}"
    mkdir -p "${STAGING_DMG_DIR}"
    unzip -o "${VPK_PORTABLE_ZIP}" -d "${STAGING_DMG_DIR}" || echo "Warning: unzip failed for ${VPK_PORTABLE_ZIP}" 
else
    echo "Info: Velopack portable zip not found at ${VPK_PORTABLE_ZIP}; skipping unzip and DMG creation."
fi

# Prepare DMG background if provided
echo "Setting up DMG background..."
if [ -n "${BACKGROUND_IMAGE:-}" ] && [ -f "${BACKGROUND_IMAGE}" ]; then
    echo "Including background image ${BACKGROUND_IMAGE} in DMG staging"
    mkdir -p "${STAGING_DMG_DIR}/.background"
    # copy and normalize name to a common filename used by create-dmg
    cp "${BACKGROUND_IMAGE}" "${STAGING_DMG_DIR}/.background/background.jpg"
else
    echo "No background image found at ${BACKGROUND_IMAGE:-'(none)'}; DMG will use default background"
fi

# Prepare create-dmg notarization args
NOTARIZE_ARG=()
if [ -n "${NOTARIZE_CREDENTIALS:-}" ]; then
    NOTARIZE_ARG=(--notarize "${NOTARIZE_CREDENTIALS}")
fi

echo "Creating DMG from staging dir: ${STAGING_DMG_DIR} -> ${PUBLISH_DIR}/${DMG_NAME}"
if command -v create-dmg >/dev/null 2>&1; then
    create-dmg \
            --volname "${APP_NAME}" \
            --window-pos 200 120 \
            --window-size 600 350 \
            --icon-size 128 \
            --app-drop-link 425 150 \
            --icon "${APP_NAME}.app" 175 150 \
            --background "${STAGING_DMG_DIR}/.background/background.jpg" \
            --notarize "${NOTARIZE_CREDENTIALS}" \
            "${PUBLISH_DIR}/${DMG_NAME}" \
            "${STAGING_DIR}"

            #"${NOTARIZE_ARG[@]}" \
    echo "DMG creation completed: ${PUBLISH_DIR}/${DMG_NAME}"
else
    echo "create-dmg not found; skipping DMG creation. Install create-dmg and re-run the printed command if desired."
fi
