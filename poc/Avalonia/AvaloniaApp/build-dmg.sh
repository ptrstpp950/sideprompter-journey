#!/bin/bash

# Exit on error
set -e

# Clean previous builds
echo "Cleaning previous builds..."
dotnet clean -c Release

# Build for x64
echo "Building for Intel (x64)..."
#dotnet publish -c Release -r osx-x64 --self-contained true
dotnet publish -c Release -r osx-x64 --self-contained true -p:graph=true -p:PublishReadyToRun=false -p:PublishTrimmed=true

# Build for arm64
echo "Building for Apple Silicon (arm64)..."
#dotnet publish -c Release -r osx-arm64 --self-contained true
dotnet publish -c Release -r osx-arm64 --self-contained true -p:graph=true -p:PublishReadyToRun=false -p:PublishTrimmed=true

# Create output directory
UNIVERSAL_DIR="bin/Release/net9.0-macos/universal"
mkdir -p "$UNIVERSAL_DIR"

# Copy the x64 app as the base
echo "Creating universal app bundle..."
cp -R "bin/Release/net9.0-macos/osx-x64/AvaloniaApp.app" "$UNIVERSAL_DIR/"

# Use lipo to create universal binaries for all dylibs and the main executable
find "bin/Release/net9.0-macos/osx-x64/AvaloniaApp.app" -name "*.dylib" | while read x64_lib; do
    rel_path=${x64_lib#bin/Release/net9.0-macos/osx-x64/AvaloniaApp.app/}
    arm64_lib="bin/Release/net9.0-macos/osx-arm64/AvaloniaApp.app/$rel_path"
    universal_lib="$UNIVERSAL_DIR/AvaloniaApp.app/$rel_path"
    
    if [ -f "$arm64_lib" ]; then
        # Check architectures before combining
        x64_arch=$(lipo -archs "$x64_lib")
        arm64_arch=$(lipo -archs "$arm64_lib")
        
        if [ "$x64_arch" == "$arm64_arch" ]; then
            echo "⚠️ Skipping $rel_path - both files have the same architecture: $x64_arch"
            # Just copy the x64 version as it already exists in the universal dir
        else
            echo "Creating universal binary for $rel_path"
            lipo -create "$x64_lib" "$arm64_lib" -output "$universal_lib" || {
                echo "⚠️ Failed to create universal binary for $rel_path, copying x64 version instead"
                # If lipo fails, use the x64 version
                cp "$x64_lib" "$universal_lib"
            }
        fi
    fi
done

# Create universal binary for the main executable
MAIN_EXEC="Contents/MacOS/AvaloniaApp"
x64_exec="bin/Release/net9.0-macos/osx-x64/AvaloniaApp.app/$MAIN_EXEC"
arm64_exec="bin/Release/net9.0-macos/osx-arm64/AvaloniaApp.app/$MAIN_EXEC"
universal_exec="$UNIVERSAL_DIR/AvaloniaApp.app/$MAIN_EXEC"

# Check architectures of main executable
x64_arch=$(lipo -archs "$x64_exec")
arm64_arch=$(lipo -archs "$arm64_exec")

if [ "$x64_arch" == "$arm64_arch" ]; then
    echo "⚠️ Main executable has the same architecture in both builds: $x64_arch"
    # The x64 version is already in the universal directory
else
    echo "Creating universal binary for main executable"
    lipo -create "$x64_exec" "$arm64_exec" -output "$universal_exec" || {
        echo "⚠️ Failed to create universal binary for main executable, keeping x64 version"
    }
fi

# Verify the architecture of the main executable
echo "Verifying architectures of final application:"
echo "Main executable: $(lipo -archs "$UNIVERSAL_DIR/AvaloniaApp.app/$MAIN_EXEC")"

# Find all dylibs and check their architecture
echo "Checking architectures of libraries in the universal app:"
find "$UNIVERSAL_DIR/AvaloniaApp.app" -name "*.dylib" | sort | head -5 | while read lib; do
    rel_path=${lib#$UNIVERSAL_DIR/AvaloniaApp.app/}
    echo "- $rel_path: $(lipo -archs "$lib")"
done
echo "... (and more libraries)"

# Create a DMG file for easy distribution
echo "Creating DMG image of the universal app..."
APP_NAME="AvaloniaApp"
APP_PATH="$UNIVERSAL_DIR/$APP_NAME.app"
DMG_FILE="$UNIVERSAL_DIR/$APP_NAME-Universal.dmg"
DMG_TEMP="$UNIVERSAL_DIR/pack.temp.dmg"
DMG_VOLUME="$APP_NAME Installer"
DMG_SIZE=500m

# Create a temporary directory for DMG contents
STAGING_DIR="$UNIVERSAL_DIR/staging"
mkdir -p "$STAGING_DIR"
cp -R "$APP_PATH" "$STAGING_DIR"

# Create a symlink to /Applications
pushd "$STAGING_DIR" > /dev/null
ln -s /Applications Applications
popd > /dev/null

# Create a temporary DMG
hdiutil create -volname "$DMG_VOLUME" -srcfolder "$STAGING_DIR" -ov -format UDRW "$DMG_TEMP"

# Mount the temporary DMG
DEVICE=$(hdiutil attach -readwrite -noverify -noautoopen "$DMG_TEMP" | grep "Apple_HFS" | cut -d ' ' -f 1)

# Give it some time to mount
sleep 3

# Optional: Set a custom icon for the volume
# This requires a .VolumeIcon.icns file
# cp /path/to/your/VolumeIcon.icns "/Volumes/$DMG_VOLUME/.VolumeIcon.icns"
# SetFile -a C "/Volumes/$DMG_VOLUME"

# Optional: Set the background image 
# This requires a background image
# mkdir -p "/Volumes/$DMG_VOLUME/.background"
# cp /path/to/your/background.png "/Volumes/$DMG_VOLUME/.background/background.png"

# Optional: Position icons on the DMG
# This requires applescript
# osascript <<EOF
# tell application "Finder"
#     tell disk "$DMG_VOLUME"
#         open
#         set current view of container window to icon view
#         set toolbar visible of container window to false
#         set statusbar visible of container window to false
#         set the bounds of container window to {400, 100, 900, 450}
#         set theViewOptions to the icon view options of container window
#         set arrangement of theViewOptions to not arranged
#         set icon size of theViewOptions to 72
#         set background picture of theViewOptions to file ".background:background.png"
#         set position of item "$APP_NAME.app" of container window to {120, 180}
#         set position of item "Applications" of container window to {380, 180}
#         close
#         open
#         update without registering applications
#         delay 5
#         close
#     end tell
# end tell
# EOF

# Unmount the temporary DMG
hdiutil detach "$DEVICE"

# Convert the temporary DMG to the final compressed DMG
hdiutil convert "$DMG_TEMP" -format UDZO -imagekey zlib-level=9 -o "$DMG_FILE"
rm -f "$DMG_TEMP"

# Clean up the staging directory
rm -rf "$STAGING_DIR"

echo "Universal app created successfully in $UNIVERSAL_DIR"
echo "- Universal App: $UNIVERSAL_DIR/$APP_NAME.app"
echo "- DMG Installer: $DMG_FILE"
