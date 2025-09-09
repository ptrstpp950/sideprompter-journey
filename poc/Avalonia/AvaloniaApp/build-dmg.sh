#!/bin/bash

# SIGN_ID="Apple Development: ptrstpp950@gmail.com (3MF59T34B3)" ./build-dmg.sh

set -e  # Exit on any error

# Set variables
APP_NAME="AvaloniaApp"
BUNDLE_ID="com.sideprompter.app"
VERSION="0.0.1"
DMG_NAME="${APP_NAME}-${VERSION}.dmg"
STAGING_DIR="./bin/dmg-staging"
APP_BUNDLE_PATH="${STAGING_DIR}/${APP_NAME}.app"
PUBLISH_DIR="bin/publish"
BACKGROUND_IMAGE="installer_background.jpg"
TEMP_DMG="${APP_NAME}-temp.dmg"
MOUNT_DIR="/Volumes/${APP_NAME}"
APP_PROJECT="AvaloniaApp.csproj"
ENTITLEMENTS="AvaloniaApp.entitlements"
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

# Require SIGN_ID to be provided. Exit early if it's not set so we don't
# proceed without a signing identity.
if [ -z "${SIGN_ID}" ]; then
    echo "Error: SIGN_ID is not set. Please set SIGN_ID to your code signing identity and re-run."
    echo "Example: SIGN_ID=\"Apple Development: Your Name (TEAMID)\" ./build-dmg.sh"
    exit 1
fi


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

# Temporarily commented out to avoid long build times during testing
# dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-x64 -o "${X64_OUTPUT}"
# dotnet publish "${APP_PROJECT}" -c Release --self-contained -r osx-arm64 -o "${ARM_OUTPUT}"

# NOTE: runtime-specific library copying is done later, after the app bundle
# paths (X64_APP_PATH and ARM_APP_PATH) are known. This avoids referencing
# those variables before they are defined and ensures we copy from the
# correct locations inside each built app bundle.


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

# Copy runtime-specific libraries from each built app into that app's
# Contents/MonoBundle/runtimes/ folder (merge macos-x64 and macos-arm64
# into the runtimes/ folder inside each app bundle). This makes it easy
# for the later lipo loop to find arch-specific binaries to merge.
copy_runtimes_for_arch() {
    app_path="$1"
    arch="$2" # expected: macos-x64 or macos-arm64
    if [ ! -d "${app_path}" ]; then
        return
    fi
    # Source arch folder inside the built app bundle
    src_arch_dir="${app_path}/Contents/MonoBundle/runtimes/${arch}"
    # Target is Contents/MonoBundle (per your request)
    target_dir="${app_path}/Contents/MonoBundle"
    if [ -d "${src_arch_dir}" ]; then
        echo "Copying ${arch} files from ${src_arch_dir} into ${target_dir} for app ${app_path}..."
        mkdir -p "${target_dir}"
        if command -v rsync >/dev/null 2>&1; then
            rsync -a "${src_arch_dir}/" "${target_dir}/" || cp -R "${src_arch_dir}"/* "${target_dir}/" 2>/dev/null || true
        else
            cp -R "${src_arch_dir}/"* "${target_dir}/" 2>/dev/null || true
        fi
    else
        echo "No ${arch} runtime folder in ${app_path}; skipping."
    fi
}

# Copy only the matching arch runtimes into each app bundle's Contents/MonoBundle
# so the lipo merging later will see the correct per-architecture binaries.
if [ -d "${X64_APP_PATH}" ]; then
    copy_runtimes_for_arch "${X64_APP_PATH}" "macos-x64"
fi
if [ -d "${ARM_APP_PATH}" ]; then
    copy_runtimes_for_arch "${ARM_APP_PATH}" "macos-arm64"
fi

if [ -d "${X64_APP_PATH}" ] && [ -d "${ARM_APP_PATH}" ]; then
    echo "Both x64 and arm64 builds present. Creating universal app..."
    # Copy x64 bundle as base
    cp -R "${X64_APP_PATH}" "${UNIVERSAL_APP_STAGING}"

    echo "Making binaries and dylibs universal..."
    find "${ARM_APP_PATH}" -type f | while read -r arm_file; do
        relative_path="${arm_file#${ARM_APP_PATH}/}"
        universal_file="${UNIVERSAL_APP_STAGING}/${relative_path}"
        x64_file="${X64_APP_PATH}/${relative_path}"

        # If file doesn't exist in the base (x64), copy it from arm.
        if [ ! -f "${x64_file}" ]; then
            echo "Copying arm64-only file: ${relative_path}"
            mkdir -p "$(dirname "${universal_file}")"
            cp "${arm_file}" "${universal_file}"
        # If it's a Mach-O file, merge it, but only if the architectures are different
        elif file -b "${arm_file}" | grep -q "Mach-O"; then
            x64_arch=$(lipo -info "${x64_file}" | awk -F ": " '{print $NF}')
            arm_arch=$(lipo -info "${arm_file}" | awk -F ": " '{print $NF}')
            if [ "$x64_arch" != "$arm_arch" ]; then
                echo "lipo: Merging ${relative_path}"
                lipo -create -output "${universal_file}" "${x64_file}" "${arm_file}" || {
                    echo "Warning: lipo failed for ${relative_path}. The universal build might be incomplete."
                }
            else
                echo "Skipping lipo for ${relative_path} as architectures are the same."
            fi
        fi
        # For non-Mach-O files that exist in both, we keep the x64 version. This is the default from the initial copy.
    done

    # Make sure all executables are executable
    find "${UNIVERSAL_APP_STAGING}/Contents/MacOS" -type f -exec chmod +x {} +
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
# link error \(create-dmg tries to create the same link and fails with "File exists"\).

printf "%s\n" "Ensuring create-dmg is installed (Homebrew will be used if necessary)..."

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
    # Helper to sign a file and continue on failure
    sign_file() {
        fpath="$1"
        echo "Signing: ${fpath}"
        codesign --sign "$SIGN_ID" --options runtime --timestamp --force "${fpath}" || echo "Warning: failed to sign ${fpath}"
    }

    # 1) Sign native libraries first (MonoBundle and runtimes)
    if [ -d "${UNIVERSAL_APP_STAGING}/Contents/MonoBundle" ]; then
        find "${UNIVERSAL_APP_STAGING}/Contents/MonoBundle" -type f \( -name '*.dylib' -o -name '*.so' -o -name '*.jnilib' \) -print0 | while IFS= read -r -d '' f; do
            sign_file "$f"
        done
    fi

    # 2) Sign framework dylibs inside Contents/Frameworks
    if [ -d "${UNIVERSAL_APP_STAGING}/Contents/Frameworks" ]; then
        find "${UNIVERSAL_APP_STAGING}/Contents/Frameworks" -type f -name '*.dylib' -print0 | while IFS= read -r -d '' fw; do
            sign_file "$fw"
        done
    fi

    # 3) Sign executables in Contents/MacOS
    if [ -d "${UNIVERSAL_APP_STAGING}/Contents/MacOS" ]; then
        find "${UNIVERSAL_APP_STAGING}/Contents/MacOS" -type f -print0 | while IFS= read -r -d '' exe; do
            # Only attempt to sign regular files (skip symlinks)
            if [ -f "$exe" ]; then
                sign_file "$exe"
            fi
        done
    fi

    # 4) Sign any nested helpers or tools under Contents/Helpers or similar
    find "${UNIVERSAL_APP_STAGING}" -type d \( -path '*/Helpers' -o -path '*/Helpers/*' -o -path '*/Contents/Library/*' \) -prune -o -type f \( -name '*.dylib' -o -name '*.so' -o -path '*/Contents/MacOS/*' \) -print0 2>/dev/null | while IFS= read -r -d '' f; do
        # already signed above, but this is a safety net; only sign if not signed
        if ! codesign --verify --verbose=0 "$f" >/dev/null 2>&1; then
            sign_file "$f"
        fi
    done || true

    # 5) Now sign the top-level bundle (do not use --deep; nested items were signed)
    if [ -f "${ENTITLEMENTS}" ]; then
        echo "Signing app bundle with entitlements: ${ENTITLEMENTS}"
        codesign --sign "$SIGN_ID" --options runtime --entitlements "${ENTITLEMENTS}" --timestamp --force "${UNIVERSAL_APP_STAGING}" || echo "Warning: failed to sign app bundle"
    else
        echo "Signing app bundle (no entitlements file found)"
        codesign --sign "$SIGN_ID" --options runtime --timestamp --force "${UNIVERSAL_APP_STAGING}" || echo "Warning: failed to sign app bundle"
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
