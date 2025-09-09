#!/bin/bash

# This script verifies a given AvaloniaApp DMG.
# It checks if the main executable is universal and if .dylib files have correct architectures.
# It reports all architecture errors it finds before exiting.

# --- Configuration ---
APP_NAME="AvaloniaApp"
MOUNT_DIR="/Volumes/${APP_NAME}"

# --- Argument Handling ---
if [ -z "$1" ]; then
  echo "🔴 ERROR: No DMG path provided."
  echo "Usage: $0 <path_to_dmg_file>"
  exit 1
fi
DMG_PATH="$1"

# --- Functions ---
cleanup() {
  echo "---"
  echo "Cleaning up..."
  if [ -d "${MOUNT_DIR}" ]; then
    echo "Unmounting DMG from ${MOUNT_DIR}..."
    hdiutil detach "${MOUNT_DIR}" -force >/dev/null 2>&1 || true
  fi
  echo "Cleanup complete."
}

# --- Main Script ---
trap cleanup EXIT HUP INT QUIT TERM

FINAL_EXIT_CODE=0

echo "--- Starting DMG Verification for ${DMG_PATH} ---"

# 1. Verify DMG file exists
echo "STEP 1: Verifying DMG file exists..."
if [ ! -f "${DMG_PATH}" ]; then
  echo "🔴 ERROR: DMG file not found at ${DMG_PATH}"
  exit 1
fi
echo "✅ DMG file found."
echo "---"

# 2. Mount the DMG
echo "STEP 2: Mounting DMG..."
hdiutil detach "${MOUNT_DIR}" -force >/dev/null 2>&1 || true
hdiutil attach "${DMG_PATH}" -readonly
if [ ! -d "${MOUNT_DIR}" ]; then
  echo "🔴 ERROR: Failed to mount DMG."
  exit 1
fi
echo "✅ DMG mounted successfully at ${MOUNT_DIR}"
echo "---"

# 3. Verify Architectures
echo "STEP 3: Verifying binary architectures..."
APP_BUNDLE_PATH="${MOUNT_DIR}/${APP_NAME}.app"
MAIN_EXEC_PATH="${APP_BUNDLE_PATH}/Contents/MacOS/${APP_NAME}"

# Verify Main Executable
echo "Checking main executable:"
if [ ! -f "${MAIN_EXEC_PATH}" ]; then
  echo "🔴 ERROR: Main executable not found at ${MAIN_EXEC_PATH}"
  FINAL_EXIT_CODE=1
else
  LIPO_INFO=$(lipo -info "${MAIN_EXEC_PATH}")
  echo "  ${MAIN_EXEC_PATH}"
  echo "  ${LIPO_INFO}"

  if ! echo "${LIPO_INFO}" | grep -q "x86_64" || ! echo "${LIPO_INFO}" | grep -q "arm64"; then
    echo "🔴 ERROR: Main executable is NOT a universal binary."
    FINAL_EXIT_CODE=1
  else
    echo "✅ Main executable is a universal binary."
  fi
fi
echo ""

# Verify Dylibs
echo "Checking included .dylib files:"
DYLIB_VERIFICATION_FAILED=false
while IFS= read -r dylib_path; do
  echo "  Verifying: ${dylib_path}"
  info=$(lipo -info "${dylib_path}")
  is_arm64=$(echo "$info" | grep -c "arm64")
  is_x64=$(echo "$info" | grep -c "x86_64")

  # Rule 1: Arch-specific path for arm64
  if [[ "${dylib_path}" == *"/runtimes/macos-arm64/"* || "${dylib_path}" == *"/runtimes/osx-arm64/"* ]]; then
    if [ "$is_arm64" -eq 1 ] && [ "$is_x64" -eq 0 ]; then
      echo "    ✅ OK: Correctly arm64-only."
    else
      echo "    🔴 ERROR: Expected arm64-only for this path, but got: $info"
      DYLIB_VERIFICATION_FAILED=true
    fi
  # Rule 2: Arch-specific path for x64
  elif [[ "${dylib_path}" == *"/runtimes/macos-x64/"* || "${dylib_path}" == *"/runtimes/osx-x64/"* ]]; then
    if [ "$is_x64" -eq 1 ] && [ "$is_arm64" -eq 0 ]; then
      echo "    ✅ OK: Correctly x86_64-only."
    else
      echo "    🔴 ERROR: Expected x86_64-only for this path, but got: $info"
      DYLIB_VERIFICATION_FAILED=true
    fi
  # Rule 3: Should be universal
  else
    if [ "$is_arm64" -eq 1 ] && [ "$is_x64" -eq 1 ]; then
      echo "    ✅ OK: Universal binary."
    else
      echo "    🔴 ERROR: Expected universal binary for this path, but got: $info"
      DYLIB_VERIFICATION_FAILED=true
    fi
  fi
done < <(find "${APP_BUNDLE_PATH}" -name "*.dylib")

if [ "$DYLIB_VERIFICATION_FAILED" = true ]; then
  FINAL_EXIT_CODE=1
fi

# --- Final Result ---
if [ "$FINAL_EXIT_CODE" -ne 0 ]; then
  echo "---"
  echo "🔴 Verification failed. See errors above."
else
  echo "---"
  echo "🎉 Verification successful! All binaries have the correct architecture."
fi

echo "The script will now clean up the mounted DMG."
exit $FINAL_EXIT_CODE
