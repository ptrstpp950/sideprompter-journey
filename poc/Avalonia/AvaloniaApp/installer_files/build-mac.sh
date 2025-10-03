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
RUN_PUBLISH="${RUN_PUBLISH:-1}"  # Default to publishing
VERSION="${VERSION:-}"

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
        *)
            ;;
    esac
done

# Require SIGN_ID
if [ -z "${SIGN_ID:-}" ]; then
    echo "Error: SIGN_ID is not set. Please set it and re-run."
    exit 1
fi

# Optional: allow a different signing identity for installer signing (productbuild requires an "Installer" identity)
# Example: SIGN_INSTALL_ID="Developer ID Installer: Your Name (TEAMID)"
SIGN_INSTALL_ID="${SIGN_INSTALL_ID:-}"

if [ -z "${SIGN_INSTALL_ID}" ]; then
    echo "Note: SIGN_INSTALL_ID not set. Installer packages may fail to sign during productbuild if you only have an Application signing identity."
    echo "If you see errors from productbuild about 'Could not find appropriate signing identity' supply SIGN_INSTALL_ID and re-run."
fi


# extract VERSION from csproj if not set
if [ -f "${APP_PROJECT}" ]; then
    # macOS grep doesn't support -P; use sed which is portable on macOS to extract the <Version> value
    VERSION_FROM_CSPROJ=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "${APP_PROJECT}" | head -n1 || true)
    if [ -n "${VERSION_FROM_CSPROJ}" ]; then
        VERSION="${VERSION_FROM_CSPROJ}"
        echo "Extracted version from ${APP_PROJECT}: ${VERSION}"
    else
        echo "Error: Could not extract version from ${APP_PROJECT}; using default ${VERSION}"
        exit 1
    fi
else
    echo "Error: ${APP_PROJECT} not found; using default version ${VERSION}"
    exit 1
fi

if [ -z "${VERSION}" ]; then
    echo "Error: VERSION is not set and could not be extracted. Please set it and re-run."
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

# Create a native universal launcher (Mach-O), not a shell script, so TCC recognizes the app correctly
LAUNCHER_SRC_PATH="${WRAPPER_APP_PATH}/Contents/MacOS/launcher.c"
LAUNCHER_BIN_PATH="${WRAPPER_APP_PATH}/Contents/MacOS/launcher"
cat > "${LAUNCHER_SRC_PATH}" <<'LAUNCHERC'
#include <unistd.h>
#include <limits.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <libgen.h>
#include <mach-o/dyld.h>

static int join_path(char* out, size_t outsz, const char* a, const char* b) {
    size_t la = strlen(a);
    int need_slash = (la > 0 && a[la-1] != '/');
    if (snprintf(out, outsz, need_slash ? "%s/%s" : "%s%s", a, b) >= (int)outsz) return -1;
    return 0;
}

int main(int argc, char* argv[]) {
    char self[PATH_MAX];
    ssize_t n = readlink("/proc/self/path", self, sizeof(self) - 1);
    if (n < 0) {
        // Fallback for macOS: _NSGetExecutablePath
        uint32_t sz = (uint32_t)sizeof(self);
        if (_NSGetExecutablePath(self, &sz) != 0) {
            fprintf(stderr, "Failed to get executable path\n");
            return 1;
        }
    } else {
        self[n] = '\0';
    }

    // Resolve to absolute directory of the launcher
    char resolved[PATH_MAX];
    if (realpath(self, resolved) == NULL) {
        strncpy(resolved, self, sizeof(resolved));
        resolved[sizeof(resolved)-1] = '\0';
    }
    char* dir = dirname(resolved);

    char targetRel[PATH_MAX];
#if defined(__aarch64__) || defined(__arm64__)
    strncpy(targetRel, "../Resources/SidePrompter-arm64.app/Contents/MacOS/SidePrompter", sizeof(targetRel));
#else
    strncpy(targetRel, "../Resources/SidePrompter-x64.app/Contents/MacOS/SidePrompter", sizeof(targetRel));
#endif
    targetRel[sizeof(targetRel)-1] = '\0';

    char target[PATH_MAX];
    if (join_path(target, sizeof(target), dir, targetRel) != 0) {
        fprintf(stderr, "Path too long\n");
        return 1;
    }

    // Forward arguments
    char** newargv = (char**)malloc((size_t)(argc + 1) * sizeof(char*));
    if (!newargv) {
        perror("malloc failed");
        return 1;
    }
    for (int i = 0; i < argc; i++) newargv[i] = argv[i];
    newargv[argc] = NULL;

    execv(target, newargv);
    perror("execv failed");
    return 1;
}
LAUNCHERC

# Compile a universal (arm64 + x86_64) launcher binary
if command -v clang >/dev/null 2>&1; then
    echo "Compiling universal launcher..."
    clang -Os -arch arm64 -arch x86_64 -o "${LAUNCHER_BIN_PATH}" "${LAUNCHER_SRC_PATH}" || {
        echo "Error: failed to compile launcher with clang"; exit 1; }
else
    echo "Error: clang not found. Xcode Command Line Tools are required to build the native launcher.";
    exit 1;
fi

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
echo "Signing launcher binary and wrapper app..."
sign_file "${LAUNCHER_BIN_PATH}"

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

# Run vpk if available
if command -v vpk >/dev/null 2>&1; then
    echo "vpk tool found, creating Velopack with version ${VERSION}..."
    mkdir -p ./bin/velopack
    # Determine which identity to use for installer signing. productbuild requires an "Installer" signing identity
    EFFECTIVE_INSTALL_SIGN_ID="${SIGN_INSTALL_ID:-${SIGN_ID}}"
    if [ -z "${SIGN_INSTALL_ID:-}" ]; then
        echo "Warning: SIGN_INSTALL_ID not set; falling back to SIGN_ID for installer signing. productbuild may fail if you don't have a 'Developer ID Installer' identity."
    fi

    vpk pack \
        -o ./bin/velopack \
        -c osx \
        -u sideprompter \
        -v "${VERSION}" \
        -p "${WRAPPER_APP_PATH}" \
        --plist "${WRAPPER_APP_PATH}/Contents/Info.plist" \
        --packTitle "SidePrompter" \
        --icon Assets/icon.icns \
        --signEntitlements "${ENTITLEMENTS}" \
        --signAppIdentity "${SIGN_ID}" \
        --signInstallIdentity "${EFFECTIVE_INSTALL_SIGN_ID}" \
        --notaryProfile "${NOTARIZE_CREDENTIALS:-}" \
        --mainExe launcher || echo "Warning: vpk pack failed"
else
    echo "vpk not found in PATH; skipping Velopack creation. Install 'vpk' or run the printed command manually."
fi

## : '
## # --- DMG Creation (optional) ---
## # If vpk produced a portable zip, unzip it into the staging dir and create a DMG
## VPK_PORTABLE_ZIP="./bin/velopack/sideprompter-osx-Portable.zip"
## PUBLISH_DIR="./bin/velopack"
## DMG_NAME="${APP_NAME}.dmg"
## STAGING_DMG_DIR="./bin/dmg-staging"
## BACKGROUND_IMAGE="installer_files/installer_background.jpg"
## 
## 
## mkdir -p "${PUBLISH_DIR}"
## 
## if [ -f "${VPK_PORTABLE_ZIP}" ]; then
##     echo "Unzipping Velopack portable zip: ${VPK_PORTABLE_ZIP} -> ${STAGING_DMG_DIR}"
##     mkdir -p "${STAGING_DMG_DIR}"
##     unzip -o "${VPK_PORTABLE_ZIP}" -d "${STAGING_DMG_DIR}" || echo "Warning: unzip failed for ${VPK_PORTABLE_ZIP}" 
## else
##     echo "Info: Velopack portable zip not found at ${VPK_PORTABLE_ZIP}; skipping unzip and DMG creation."
## fi
## 
## # Prepare DMG background if provided
## echo "Setting up DMG background..."
## if [ -n "${BACKGROUND_IMAGE:-}" ] && [ -f "${BACKGROUND_IMAGE}" ]; then
##     echo "Including background image ${BACKGROUND_IMAGE} in DMG staging"
##     mkdir -p "${STAGING_DMG_DIR}/.background"
##     # copy and normalize name to a common filename used by create-dmg
##     cp "${BACKGROUND_IMAGE}" "${STAGING_DMG_DIR}/.background/background.jpg"
## else
##     echo "No background image found at ${BACKGROUND_IMAGE:-'(none)'}; DMG will use default background"
## fi
## 
## # Prepare create-dmg notarization args
## NOTARIZE_ARG=()
## if [ -n "${NOTARIZE_CREDENTIALS:-}" ]; then
##     NOTARIZE_ARG=(--notarize "${NOTARIZE_CREDENTIALS}")
## fi
## 
## # Remove existing DMG if present
## if [ -f "${PUBLISH_DIR}/${DMG_NAME}" ]; then
##     echo "Removing existing DMG at ${PUBLISH_DIR}/${DMG_NAME}"
##     rm -f "${PUBLISH_DIR}/${DMG_NAME}"
## fi
## 
## echo "Creating DMG from staging dir: ${STAGING_DMG_DIR} -> ${PUBLISH_DIR}/${DMG_NAME}"
## if command -v create-dmg >/dev/null 2>&1; then
##     create-dmg \
##             --volname "${APP_NAME}" \
##             --window-pos 200 120 \
##             --window-size 600 350 \
##             --icon-size 128 \
##             --app-drop-link 425 150 \
##             --icon "${APP_NAME}.app" 175 150 \
##             --background "${STAGING_DMG_DIR}/.background/background.jpg" \
##             --notarize "${NOTARIZE_CREDENTIALS}" \
##             "${PUBLISH_DIR}/${DMG_NAME}" \
##             "${STAGING_DMG_DIR}"
## 
##             #"${NOTARIZE_ARG[@]}" \
##     echo "DMG creation completed: ${PUBLISH_DIR}/${DMG_NAME}"
## else
##     echo "create-dmg not found; skipping DMG creation. Install create-dmg and re-run the printed command if desired."
##     exit 1
## fi
## 
## # Staple the DMG if created
## if [ ! -f "${PUBLISH_DIR}/${DMG_NAME}" ]; then
##     echo "DMG not found at ${PUBLISH_DIR}/${DMG_NAME}; skipping stapling."
##     exit 1
## fi
## if ! command -v stapler >/dev/null 2>&1; then
##     echo "stapler not found; skipping stapling. Install stapler and re-run the printed command if desired."
##     exit 1
## fi
## echo "Stapling DMG: ${PUBLISH_DIR}/${DMG_NAME}"
## stapler staple "${PUBLISH_DIR}/${DMG_NAME}" || echo "Warning: stapler staple failed for ${PUBLISH_DIR}/${DMG_NAME}"
## 
## echo "Packaging complete. Output located in ./bin/velopack"
## echo "Upload it using: rclone copy ./bin/velopack/ cloudflare:sideprompter-installer/mac"
## 
## # End of script'
echo "DMG creation and stapling steps are commented out. Uncomment them in the script if DMG creation is desired."