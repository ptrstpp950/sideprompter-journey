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
#dotnet build AvaloniaApp.csproj -c Release -r osx-x64

# Check if app bundle exists
if [ ! -d "./bin/Release/net9.0-macos/osx-x64/${APP_NAME}.app" ]; then
    echo "Error: App bundle not found. Please build the app first."
    exit 1
fi

# Check if background image exists
if [ ! -f "${BACKGROUND_IMAGE}" ]; then
    echo "Error: Background image '${BACKGROUND_IMAGE}' not found."
    exit 1
fi

# Create the staging directory
mkdir -p "${STAGING_DIR}"

# Copy the app bundle to the staging directory
cp -R "./bin/Release/net9.0-macos/osx-x64/${APP_NAME}.app" "${STAGING_DIR}"

# Copy the background image to the staging directory using the conventional .background folder
mkdir -p "${STAGING_DIR}/.background"
cp "${BACKGROUND_IMAGE}" "${STAGING_DIR}/.background/background.jpg"

# Verify the background image was copied
if [ ! -f "${STAGING_DIR}/.background/background.jpg" ]; then
    echo "Error: Failed to copy background image to staging directory."
    exit 1
fi

# Create a symbolic link to the /Applications folder
ln -s /Applications "${STAGING_DIR}/Applications"

echo "Creating temporary DMG..."
# Create a temporary DMG
hdiutil create -volname "${APP_NAME}" -srcfolder "${STAGING_DIR}" -ov -format UDRW -size 200m "${TEMP_DMG}"

echo "Mounting DMG..."
# Mount the temporary DMG
hdiutil attach "${TEMP_DMG}" -mountpoint "${MOUNT_DIR}" -nobrowse

# Wait for the mount to complete
sleep 3

# Verify the background image is accessible in the mounted volume (inside .background)
if [ ! -f "${MOUNT_DIR}/.background/background.jpg" ]; then
    echo "Error: Background image not found in mounted volume."
    exit 1
fi

echo "Configuring DMG appearance..."
# Set the background image using AppleScript
if ! osascript >/dev/null <<EOF
tell application "Finder"
    tell disk "${APP_NAME}"
        open
        set current view of container window to icon view
        set toolbar visible of container window to false
        set statusbar visible of container window to false
        set bounds of container window to {400, 100, 1000, 600}
        tell icon view options of container window
            set arrangement to arranged by name
            set icon size to 72
            set text size to 12
        end tell
        try
            -- Use the Finder-style reference to the file inside the .background folder
            set background picture of icon view options of container window to file ".background:background.jpg" of container window
        on error errMsg
            log "Error setting background: " & errMsg
            -- Continue without background image
        end try
        set position of item "${APP_NAME}.app" of container window to {150, 200}
        set position of item "Applications" of container window to {450, 200}
        close
        open
        update without registering applications
        delay 2
    end tell
end tell
EOF
then
    echo "Warning: AppleScript encountered an error, but continuing..."
fi

echo "AppleScript completed."

# Give Finder a moment to process the changes
sleep 2

# Wait a bit more
sleep 2

echo "Unmounting DMG..."
# Force unmount if needed
hdiutil detach "${MOUNT_DIR}" -force 2>/dev/null || hdiutil detach "${MOUNT_DIR}" 2>/dev/null || true

# Wait for unmount
sleep 2

echo "Converting to compressed DMG..."
# Convert to compressed DMG
hdiutil convert "${TEMP_DMG}" -format UDZO -imagekey zlib-level=9 -o "${DMG_NAME}"

# Move DMG to publish folder
mv "${DMG_NAME}" "${PUBLISH_DIR}/"

# Clean up
rm -rf "${STAGING_DIR}"
rm -f "${TEMP_DMG}"

echo "Successfully created ${DMG_NAME} with background image at ${PUBLISH_DIR}"
