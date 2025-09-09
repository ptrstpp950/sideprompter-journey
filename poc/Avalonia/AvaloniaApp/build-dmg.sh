#!/bin/bash

# SIGN_ID="Apple Development: ptrstpp950@gmail.com (3MF59T34B3)" ./build-dmg.sh

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
ENTITLEMENTS="AvaloniaApp.entitlements"

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
rm -rf "${PUBLISH_DIR}"
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

# Build the Avalonia app with self-contained deployments
echo "Publishing for x64 and arm64..."
# Create PUBLISH_DIR and publish both architectures as self-contained builds
mkdir -p "${PUBLISH_DIR}"
dotnet publish "${APP_PROJECT}" -c Release -r osx-x64 --self-contained true -o "${PUBLISH_DIR}/osx-x64"
dotnet publish "${APP_PROJECT}" -c Release -r osx-arm64 --self-contained true -o "${PUBLISH_DIR}/osx-arm64"


# Check if app bundle exists
X64_APP_PATH="${PUBLISH_DIR}/osx-x64/${APP_NAME}.app"
ARM_APP_PATH="${PUBLISH_DIR}/osx-arm64/${APP_NAME}.app"

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
    # Use arm64 bundle as base (per requirements)
    cp -R "${ARM_APP_PATH}" "${UNIVERSAL_APP_STAGING}"

    # Path to executable inside .app - try to detect the main binary under Contents/MacOS
    X64_BIN="$(/bin/ls "${X64_APP_PATH}/Contents/MacOS" | head -n1)"
    ARM_BIN="$(/bin/ls "${ARM_APP_PATH}/Contents/MacOS" | head -n1)"
    BASE_BIN_NAME="${ARM_BIN}"

    if [ -z "${BASE_BIN_NAME}" ]; then
        echo "Warning: Could not detect executable inside app bundle. Skipping lipo; using arm64 bundle as-is."
    else
        X64_BIN_PATH="${X64_APP_PATH}/Contents/MacOS/${BASE_BIN_NAME}"
        ARM_BIN_PATH="${ARM_APP_PATH}/Contents/MacOS/${BASE_BIN_NAME}"
        UNIVERSAL_BIN_PATH="${UNIVERSAL_APP_STAGING}/Contents/MacOS/${BASE_BIN_NAME}"

        if command -v lipo >/dev/null 2>&1; then
            echo "Merging main executable with lipo..."
            lipo -create -output "${UNIVERSAL_BIN_PATH}" "${X64_BIN_PATH}" "${ARM_BIN_PATH}" || {
                echo "lipo failed, leaving arm64 binary in place"
            }
            chmod +x "${UNIVERSAL_BIN_PATH}"

            # Combine all native libraries in Contents/MonoBundle/
            echo "Combining native libraries in Contents/MonoBundle/..."
            if [ -d "${X64_APP_PATH}/Contents/MonoBundle" ] && [ -d "${ARM_APP_PATH}/Contents/MonoBundle" ]; then
                # Find all native libraries and executables to combine
                find "${ARM_APP_PATH}/Contents/MonoBundle" -type f \( -name '*.dylib' -o -name '*.so' -o -executable \) | while IFS= read -r arm_lib; do
                    # Get relative path from MonoBundle
                    rel_path="${arm_lib#${ARM_APP_PATH}/Contents/MonoBundle/}"
                    x64_lib="${X64_APP_PATH}/Contents/MonoBundle/${rel_path}"
                    universal_lib="${UNIVERSAL_APP_STAGING}/Contents/MonoBundle/${rel_path}"
                    
                    if [ -f "${x64_lib}" ]; then
                        echo "Combining ${rel_path}..."
                        # Check if files are actually different architectures before lipo
                        if file "${arm_lib}" | grep -q "Mach-O" && file "${x64_lib}" | grep -q "Mach-O"; then
                            lipo -create -output "${universal_lib}" "${x64_lib}" "${arm_lib}" || {
                                echo "Warning: lipo failed for ${rel_path}, keeping arm64 version"
                            }
                            # Copy permissions from original file
                            if [ -n "$(command -v stat)" ]; then
                                chmod "$(stat -f '%Mp%Lp' "${arm_lib}")" "${universal_lib}" 2>/dev/null || chmod +x "${universal_lib}"
                            else
                                chmod +x "${universal_lib}"
                            fi
                        fi
                    fi
                done
            else
                echo "Warning: MonoBundle directory not found in one or both builds"
            fi
        else
            echo "lipo not available; skipping universal binary creation."
        fi
    fi
else
    # Copy whichever build exists into staging
    if [ -d "${ARM_APP_PATH}" ]; then
        echo "Only arm64 build found. Copying arm64 bundle to staging."
        cp -R "${ARM_APP_PATH}" "${UNIVERSAL_APP_STAGING}"
    elif [ -d "${X64_APP_PATH}" ]; then
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

# Signing / Notarization configuration
# Set these environment variables before running the script if you want signing/notarization:
# SIGN_ID - the codesign identity (e.g. "Developer ID Application: Your Name (TEAMID)")
# NOTARY_KEY_PATH - path to the API key .p8 for notarytool (optional)
# NOTARY_KEY_ID - key id for notarytool (optional)
# NOTARY_ISSUER - issuer/team id for notarytool (optional)

SIGN_ID="${SIGN_ID:-}"
NOTARY_KEY_PATH="${NOTARY_KEY_PATH:-}"
NOTARY_KEY_ID="${NOTARY_KEY_ID:-}"
NOTARY_ISSUER="${NOTARY_ISSUER:-}"

# If signing is configured, sign nested helper(s) and then the app bundle
if [ -n "$SIGN_ID" ]; then
    echo "Signing bundle and nested helpers with identity: $SIGN_ID"

    echo "Signing nested native libraries and executables under the app bundle"
    # Find common native items to sign: .dylib, .so, .jnilib, frameworks and executables in Contents/MacOS
    # Use a conservative list so we don't attempt to sign non-native files.
    find "${UNIVERSAL_APP_STAGING}" -type f \( -name '*.dylib' -o -name '*.so' -o -name '*.jnilib' -o -path '*/Contents/MacOS/*' -o -path '*/Resources/libs/*/bin/*' \) -print0 | while IFS= read -r -d '' file; do
        echo "Signing nested file: $file"
        codesign --sign "$SIGN_ID" --options runtime --timestamp --force "$file" || echo "Warning: failed to sign $file"
    done

    # Also sign frameworks directories (if any). Sign each binary within Frameworks if present.
    if [ -d "${UNIVERSAL_APP_STAGING}/Contents/Frameworks" ]; then
        find "${UNIVERSAL_APP_STAGING}/Contents/Frameworks" -type f -name '*.dylib' -print0 | while IFS= read -r -d '' fw; do
            echo "Signing framework dylib: $fw"
            codesign --sign "$SIGN_ID" --options runtime --timestamp --force "$fw" || echo "Warning: failed to sign $fw"
        done
    fi

    # Sign the top-level bundle. Use entitlements if present.
    if [ -f "${ENTITLEMENTS}" ]; then
        echo "Signing app bundle with entitlements: ${ENTITLEMENTS}"
        codesign --sign "$SIGN_ID" --options runtime --entitlements "${ENTITLEMENTS}" --timestamp --force --deep "${UNIVERSAL_APP_STAGING}" || echo "Warning: failed to sign app bundle"
    else
        echo "Signing app bundle (no entitlements file found)"
        codesign --sign "$SIGN_ID" --options runtime --timestamp --force --deep "${UNIVERSAL_APP_STAGING}" || echo "Warning: failed to sign app bundle"
    fi

    # Verify signature
    codesign -dv --verbose=4 "${UNIVERSAL_APP_STAGING}" || true
    spctl -a -vv "${UNIVERSAL_APP_STAGING}" || true
fi

# Notarize if credentials are provided
if [ -n "$NOTARY_KEY_PATH" ] && [ -n "$NOTARY_KEY_ID" ] && [ -n "$NOTARY_ISSUER" ]; then
    echo "Notarizing app using notarytool (this may take several minutes)"
    # Create a zip of the app for notarization
    echo "Creating zip for notarization"
    (cd "${STAGING_DIR}" && zip -r "${PUBLISH_DIR}/${APP_NAME}.zip" "${APP_NAME}.app")

    echo "Submitting to notarytool..."
    xcrun notarytool submit "${PUBLISH_DIR}/${APP_NAME}.zip" --key "$NOTARY_KEY_PATH" --key-id "$NOTARY_KEY_ID" --issuer "$NOTARY_ISSUER" --wait || {
        echo "Notarization failed or notarytool not available; please notarize manually or check credentials"
    }

    echo "Stapling notarization result to the app"
    xcrun stapler staple "${UNIVERSAL_APP_STAGING}" || echo "Warning: stapler failed"

    # Optionally remove the zip
    rm -f "${PUBLISH_DIR}/${APP_NAME}.zip"
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
