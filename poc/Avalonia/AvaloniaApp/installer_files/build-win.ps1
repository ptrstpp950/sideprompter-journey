param(
    [Alias('v')]
    [Parameter(Mandatory=$true)]
    [string]$Version
)


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

# Package using vpk, using provided version (accepts -v or -Version)
vpk pack --packId SidePrompter --packVersion $Version --packDir .\bin\publish --mainExe SidePrompter.exe --icon .\Assets\icon.ico

Pop-Location