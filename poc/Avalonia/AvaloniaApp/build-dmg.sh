#!/bin/bash

set -e  # Exit on any error

# Set variables
APP_NAME="AvaloniaApp"
BUNDLE_ID="com.sideprompter.app"
VERSION="1.0.0"
DMG_NAME="${APP_NAME}-${VERSION}.dmg"
STAGING_DIR="./bin/dmg-staging"
APP_BUNDLE_PATH="${STAGING_DIR}/${APP_NAME}.app"
PUBLISH_DIR="bin/publish"
BACKGROUND_IMAGE="installer_background.jpg"
TEMP_DMG="${APP_NAME}-temp.dmg"
MOUNT_DIR="/Volumes/${APP_NAME}"
APP_PROJECT="AvaloniaApp.csproj"
# Publish output folders for each architecture
X64_OUTPUT="./bin/Release/net9.0-macos/osx-x64"
ARM_OUTPUT="./bin/Release/net9.0-macos/osx-arm64"

# Function to clean up on error
cleanup() {
    echo "Cleaning up..."
    if [ -d "${MOUNT_DIR}" ]; then
        hdiutil detach "${MOUNT_DIR}" -force 2>/dev/null || true
    fi
    rm -rf "${STAGING_DIR}"
    rm -f "${TEMP_DMG}"
    rm -f "${DMG_NAME}"
}

# Set trap to clean up on error
trap cleanup ERR

# Clean up previous build artifacts
rm -rf "${STAGING_DIR}"
rm -f "${PUBLISH_DIR}/${DMG_NAME}"
rm -f "${TEMP_DMG}"
mkdir -p "${PUBLISH_DIR}"

# Unmount any existing DMG volumes that might be left over
echo "Cleaning up any existing DMG mounts..."
# Force unmount all AvaloniaApp related volumes
for vol in /Volumes/AvaloniaApp*; do
    if [ -d "$vol" ]; then
        echo "Unmounting $vol..."
        hdiutil detach "$vol" -force 2>/dev/null || true
        umount "$vol" 2>/dev/null || true
        rm -rf "$vol" 2>/dev/null || true
    fi
done
# Also try to unmount the specific mount point
hdiutil detach "${MOUNT_DIR}" -force 2>/dev/null || true
umount "${MOUNT_DIR}" 2>/dev/null || true
rm -rf "${MOUNT_DIR}" 2>/dev/null || true

# Verify cleanup was successful
if [ -d "${MOUNT_DIR}" ]; then
    echo "Warning: Could not clean up ${MOUNT_DIR}, this might cause issues."
else
    echo "Mount directory cleaned up successfully."
fi

# Build the Avalonia app (uncomment if needed)
echo "Publishing for x64 and arm64..."
# Publish for both architectures so the script can create a universal binary
#dotnet publish "${APP_PROJECT}" -c Release -r osx-x64 -o "${X64_OUTPUT}"
#dotnet publish "${APP_PROJECT}" -c Release -r osx-arm64 -o "${ARM_OUTPUT}"


# Check if app bundle exists
X64_APP_PATH="${X64_OUTPUT}/${APP_NAME}.app"
ARM_APP_PATH="${ARM_OUTPUT}/${APP_NAME}.app"

# Ensure at least one build exists
if [ ! -d "${X64_APP_PATH}" ] && [ ! -d "${ARM_APP_PATH}" ]; then
    echo "Error: Neither x64 nor arm64 app bundles were found. Please build the app first."
    exit 1
fi

# Prepare a universal app if possible
UNIVERSAL_APP_STAGING="${STAGING_DIR}/${APP_NAME}.app"
mkdir -p "${STAGING_DIR}"

if [ -d "${X64_APP_PATH}" ] && [ -d "${ARM_APP_PATH}" ]; then
    echo "Both x64 and arm64 builds present. Creating universal app..."
    # Copy x64 bundle as base
    cp -R "${X64_APP_PATH}" "${UNIVERSAL_APP_STAGING}"

    # Path to executable inside .app - try to detect the main binary under Contents/MacOS
    X64_BIN="$(/bin/ls "${X64_APP_PATH}/Contents/MacOS" | head -n1)"
    ARM_BIN="$(/bin/ls "${ARM_APP_PATH}/Contents/MacOS" | head -n1)"
    BASE_BIN_NAME="${X64_BIN}"

    if [ -z "${BASE_BIN_NAME}" ]; then
        echo "Warning: Could not detect executable inside app bundle. Skipping lipo; using x64 bundle as-is."
    else
        X64_BIN_PATH="${X64_APP_PATH}/Contents/MacOS/${BASE_BIN_NAME}"
        ARM_BIN_PATH="${ARM_APP_PATH}/Contents/MacOS/${BASE_BIN_NAME}"
        UNIVERSAL_BIN_PATH="${UNIVERSAL_APP_STAGING}/Contents/MacOS/${BASE_BIN_NAME}"

        if command -v lipo >/dev/null 2>&1; then
            echo "Merging binaries with lipo..."
            lipo -create -output "${UNIVERSAL_BIN_PATH}" "${X64_BIN_PATH}" "${ARM_BIN_PATH}" || {
                echo "lipo failed, leaving x64 binary in place"
            }
            chmod +x "${UNIVERSAL_BIN_PATH}"
        else
            echo "lipo not available; skipping universal binary creation."
        fi
    fi
else
    # Copy whichever build exists into staging
    if [ -d "${ARM_APP_PATH}" ]; then
        echo "Only arm64 build found. Copying arm64 bundle to staging."
        cp -R "${ARM_APP_PATH}" "${UNIVERSAL_APP_STAGING}"
    else
        echo "Only x64 build found. Copying x64 bundle to staging."
        cp -R "${X64_APP_PATH}" "${UNIVERSAL_APP_STAGING}"
    fi
fi

# Check if background image exists
if [ ! -f "${BACKGROUND_IMAGE}" ]; then
    echo "Error: Background image '${BACKGROUND_IMAGE}' not found."
    exit 1
fi

# At this point the universal (or single-arch) app bundle should be at ${UNIVERSAL_APP_STAGING}
if [ ! -d "${UNIVERSAL_APP_STAGING}" ]; then
    echo "Error: expected app bundle at ${UNIVERSAL_APP_STAGING} but not found"
    exit 1
fi

# Copy the background image to the staging directory using the conventional .background folder
mkdir -p "${STAGING_DIR}/.background"
cp "${BACKGROUND_IMAGE}" "${STAGING_DIR}/.background/background.jpg"

# Verify the background image was copied
if [ ! -f "${STAGING_DIR}/.background/background.jpg" ]; then
    echo "Error: Failed to copy background image to staging directory."
    exit 1
fi

# Do not create an Applications symlink in the staging folder.
# The create-dmg tool will create the Applications link inside the mounted image when
# we pass --app-drop-link, and creating it in the source folder causes a duplicate
# link error (create-dmg tries to create the same link and fails with "File exists").

echo "Ensuring create-dmg is installed (Homebrew will be used if necessary)..."

if ! command -v create-dmg >/dev/null 2>&1; then
    if command -v brew >/dev/null 2>&1; then
        echo "Installing create-dmg via Homebrew..."
        brew install create-dmg
    else
        echo "Error: create-dmg not found and Homebrew is not available. Please install Homebrew and run: brew install create-dmg"
        exit 1
    fi
fi

echo "Using create-dmg to produce a polished DMG from staging directory..."
# Create the DMG
create-dmg \
  --volname "${APP_NAME}" \
  --background "${STAGING_DIR}/.background/background.jpg" \
  --window-pos 200 120 \
  --window-size 800 400 \
  --icon-size 100 \
  --icon "${APP_NAME}.app" 200 190 \
  --hide-extension "${APP_NAME}.app" \
  --app-drop-link 600 185 \
  "${PUBLISH_DIR}/${DMG_NAME}" \
  "${STAGING_DIR}"

if [ -f "${PUBLISH_DIR}/${DMG_NAME}" ]; then
    echo "Successfully created ${DMG_NAME} at ${PUBLISH_DIR}"
else
    echo "create-dmg failed to produce a DMG in ${PUBLISH_DIR}"
    exit 1
fi

# Clean up staging
rm -rf "${STAGING_DIR}"
exit 0
