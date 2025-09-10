#!/bin/bash

# This script produces a DMG with a guided installer that lets users install
# the correct app bundle (x64 or arm64) for their machine.
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

# Only run cleanup on error.
trap cleanup ERR

echo "Preparing directories..."
rm -rf "${STAGING_DIR}"
rm -rf "${PUBLISH_DIR}"
mkdir -p "${STAGING_DIR}"
mkdir -p "${PUBLISH_DIR}"

# The script assumes the apps have been published.
#dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-x64 -o "${X64_OUTPUT}"
#dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-arm64 -o "${ARM_OUTPUT}"

X64_APP_PATH="${X64_OUTPUT}/${APP_NAME}.app"
ARM_APP_PATH="${ARM_OUTPUT}/${APP_NAME}.app"

if [ ! -d "${X64_APP_PATH}" ] && [ ! -d "${ARM_APP_PATH}" ]; then
    echo "Error: Neither x64 nor arm64 app bundles were found. Please build the app first."
    exit 1
fi

# Helper to copy arch-specific runtimes into the app bundle.
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
        if command -v rsync >/dev/null 2>&1;
 then
            rsync -a "${src_arch_dir}/" "${target_dir}/" || cp -R "${src_arch_dir}"/* "${target_dir}/" 2>/dev/null || true
        else
            cp -R "${src_arch_dir}"/* "${target_dir}/" 2>/dev/null || true
        fi
    else
        echo "No ${arch} runtime folder in ${app_path}; skipping."
    fi
}

# Create a hidden directory inside the staging area for the app bundles
HIDDEN_APP_DIR="${STAGING_DIR}/.apps"
mkdir -p "${HIDDEN_APP_DIR}"

STAGED_X64_APP="${HIDDEN_APP_DIR}/${APP_NAME}-x64.app"
STAGED_ARM_APP="${HIDDEN_APP_DIR}/${APP_NAME}-arm64.app"

if [ -d "${X64_APP_PATH}" ]; then
    echo "Staging x64 app to hidden directory..."
    cp -R "${X64_APP_PATH}" "${STAGED_X64_APP}"
    copy_runtimes_for_arch "${STAGED_X64_APP}" "macos-x64"
fi

if [ -d "${ARM_APP_PATH}" ]; then
    echo "Staging arm64 app to hidden directory..."
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

if [ -d "${STAGED_X64_APP}" ]; then
    sign_bundle "${STAGED_X64_APP}"
fi
if [ -d "${STAGED_ARM_APP}" ]; then
    sign_bundle "${STAGED_ARM_APP}"
fi

# Create the guided installer script
INSTALLER_SCRIPT="${STAGING_DIR}/Install ${APP_NAME}.command"
echo "Creating guided installer script at ${INSTALLER_SCRIPT}..."
cat > "${INSTALLER_SCRIPT}" <<INSTALL_SCRIPT
#!/bin/bash
# This script provides a guided installation for the user.

HERE="\$(dirname "\$0")"
OS_ARCH="\$(uname -m)"
APP_NAME="${APP_NAME}"
BUNDLE_ID="${BUNDLE_ID}"

if [ "\$OS_ARCH" = "arm64" ]; then
    TARGET_APP_NAME="${APP_NAME}-arm64.app"
    ARCH_FRIENDLY_NAME="Apple Silicon"
else
    TARGET_APP_NAME="${APP_NAME}-x64.app"
    ARCH_FRIENDLY_NAME="Intel"
fi

TARGET_APP_PATH="\$HERE/.apps/\$TARGET_APP_NAME"

if [ ! -d "\$TARGET_APP_PATH" ]; then
    /usr/bin/osascript -e "display dialog \"Error: The application bundle for your Mac\'s architecture (\$ARCH_FRIENDLY_NAME) could not be found.\" with icon stop buttons {\"OK\"} default button \"OK\""
    exit 1
fi

choice=\$(/usr/bin/osascript <<EOD
tell application (path to frontmost application as text)
    set dialogResult to display dialog "Welcome to ${APP_NAME}!\n\nThis will install the correct version for your Mac (\$ARCH_FRIENDLY_NAME)." buttons {"Install", "Run from DMG", "Cancel"} default button "Install" with icon note
    set buttonReturned to button returned of dialogResult
    return buttonReturned
end tell
EOD
)

case "\$choice" in
    "Install")
        /usr/bin/osascript -e "display notification \"Installing ${APP_NAME}...\" with title \"${APP_NAME} Installer\""
        
        # Use rsync for robust copying. It's pre-installed on macOS.
        rsync -a "\$TARGET_APP_PATH" "/Applications/"
        
        if [ \$? -eq 0 ]; then
            installed_path="/Applications/\$TARGET_APP_NAME"
            # Ask to launch the app
            launch_choice=\$(/usr/bin/osascript <<EOD
tell application (path to frontmost application as text)
    display dialog "${APP_NAME} has been successfully installed in your Applications folder." buttons {"Launch App", "OK"} default button "Launch App"
    return button returned of result
end tell
EOD
)
            if [ "\$launch_choice" = "Launch App" ]; then
                open "\$installed_path"
            fi
        else
            /usr/bin/osascript -e 'display dialog "Installation failed.\n\nCould not copy the app to /Applications. You may need to grant permissions or drag it manually from the hidden .apps folder." with icon stop'
        fi
        ;;
    "Run from DMG")
        open "\$TARGET_APP_PATH"
        ;;
    "Cancel")
        # Do nothing
        ;;
esac
INSTALL_SCRIPT
chmod +x "${INSTALLER_SCRIPT}"

# Copy background image into staging if present
if [ -f "${BACKGROUND_IMAGE}" ]; then
    mkdir -p "${STAGING_DIR}/.background"
    cp "${BACKGROUND_IMAGE}" "${STAGING_DIR}/.background/background.jpg"
fi

if ! command -v create-dmg >/dev/null 2>&1;
 then
    if command -v brew >/dev/null 2>&1;
 then
        echo "Installing create-dmg via Homebrew..."
        brew install create-dmg
    else
        echo "Error: create-dmg not found and Homebrew is not available. Please install Homebrew and run: brew install create-dmg"
        exit 1
    fi
fi

# Create the DMG with the guided installer
echo "Creating DMG with guided installer..."

# Build arguments for create-dmg
DMG_ARGS=(
  --volname "${APP_NAME}"
  --window-pos 200 120
  --window-size 600 350
  --icon-size 128
  --app-drop-link 425 150
  --icon "Install ${APP_NAME}.command" 175 150
)

if [ -f "${STAGING_DIR}/.background/background.jpg" ]; then
  DMG_ARGS+=(--background "${STAGING_DIR}/.background/background.jpg")
fi

# Execute create-dmg with options and positional arguments
create-dmg "${DMG_ARGS[@]}" "${PUBLISH_DIR}/${DMG_NAME}" "${STAGING_DIR}"

if [ -f "${PUBLISH_DIR}/${DMG_NAME}" ]; then
    echo "Successfully created ${DMG_NAME} at ${PUBLISH_DIR}"
else
    echo "create-dmg failed to produce a DMG in ${PUBLISH_DIR}"
    exit 1
fi

# final cleanup of staging
rm -rf "${STAGING_DIR}"

echo "Done."
