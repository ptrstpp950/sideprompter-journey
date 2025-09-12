param(
  [string]$Project = "src/SidePrompter/SidePrompter.csproj",
  [string]$Rid = "win-x64",
  [string]$Out = "artifacts\$($Rid)"
)

dotnet publish $Project -c Release -r $Rid --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $Out

$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-Not (Test-Path $iscc)) { Write-Error "ISCC not found. Install Inno Setup."; exit 1 }
& $iscc "build\installer\sideprompter.iss"