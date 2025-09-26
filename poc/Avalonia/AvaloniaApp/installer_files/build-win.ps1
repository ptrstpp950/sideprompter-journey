if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Error "vpk is not installed. Please install vpk using `dotnet tool install -g vpk` and ensure it's in your PATH."
    exit 1
}

# Change to the parent directory of this script so relative paths resolve correctly
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location "$ScriptDir\.."
echo "Building from directory: $PWD"

# Build and publish
dotnet publish .\AvaloniaApp.csproj -c Release --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o bin\publish

# Extract version from the built executable
$exePath = ".\bin\publish\SidePrompter.exe"
$Version = (Get-Item $exePath).VersionInfo.FileVersion

if (-not $Version) {
    Write-Error "Could not extract version information from $exePath"
    exit 1
}

#Remove last segment
$Version = ($Version -split '\.')[0..2] -join '.'
Write-Host "Built version: $Version"
#Read-Host "Press Enter to continue packaging..."

# Package using vpk, using provided version (accepts -v or -Version)
vpk pack -o .\bin\velopack --packId SidePrompter --packVersion $Version --packDir .\bin\publish --mainExe SidePrompter.exe --icon .\Assets\icon.ico

Pop-Location

Write-Host "Packaging complete. Output located in .\bin\velopack"
Write-Host "Upload it using: rclone copy .\bin\velopack\ cloudflare:sideprompter-installer/win"