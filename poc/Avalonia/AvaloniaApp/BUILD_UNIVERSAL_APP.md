# Universal macOS .app Build Script

The `build-dmg.sh` script has been updated to create universal macOS .app bundles that combine both x64 and arm64 architectures.

## Requirements Implemented

### 1. Self-contained builds
The script now publishes both runtimes as self-contained builds using the exact commands specified:
- `dotnet publish -c Release -r osx-x64 --self-contained true -o "$PUBLISH_DIR/osx-x64"`
- `dotnet publish -c Release -r osx-arm64 --self-contained true -o "$PUBLISH_DIR/osx-arm64"`

### 2. Universal .app bundle creation
- Uses the arm64 build as the base app bundle (for resources, Info.plist, etc.)
- Combines the native app hosts using `lipo`
- Combines ALL native libraries in Contents/MonoBundle/ using `lipo`

## Key Features

- **Universal binary creation**: Combines both x64 and arm64 architectures into a single .app
- **Native library combination**: Processes all .dylib, .so, and executable files in Contents/MonoBundle/
- **Smart detection**: Only attempts lipo on Mach-O binaries
- **Permission preservation**: Maintains file permissions after combination
- **Robust error handling**: Continues with single architecture if lipo fails
- **Proper cleanup**: Manages temporary directories appropriately

## Usage

```bash
# Basic usage
./build-dmg.sh

# With code signing
SIGN_ID="Apple Development: your@email.com (TEAMID)" ./build-dmg.sh

# With notarization
SIGN_ID="..." NOTARY_KEY_PATH="..." NOTARY_KEY_ID="..." NOTARY_ISSUER="..." ./build-dmg.sh
```

## Output Structure

The script creates:
- `bin/publish/osx-x64/` - x64 self-contained build
- `bin/publish/osx-arm64/` - arm64 self-contained build  
- `bin/dmg-staging/` - Universal app bundle (temporary)
- `bin/publish/AvaloniaApp-1.0.0.dmg` - Final DMG with universal app

## Verification

To verify the universal binary was created correctly:

```bash
# Check main executable
lipo -info "bin/dmg-staging/AvaloniaApp.app/Contents/MacOS/AvaloniaApp"

# Check native libraries
find "bin/dmg-staging/AvaloniaApp.app/Contents/MonoBundle" -name "*.dylib" -exec lipo -info {} \;
```

Expected output should show both x86_64 and arm64 architectures.