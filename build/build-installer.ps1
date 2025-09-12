param(
  [string]$Project = "src/SidePrompter/SidePrompter.csproj",
  [string]$Rid = "win-x64",
  [string]$Out = "artifacts$($Rid)",
  [switch]$InstallInno = $true,
  [switch]$SignInstaller = $true,
  [string]$PfxPath = "",
  [string]$PfxPassword = ""
)

# Publish self-contained single-file
Write-Host "Publishing $Project for $Rid -> $Out"
dotnet publish $Project -c Release -r $Rid --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $Out

# Ensure artifacts exist
if (-Not (Test-Path $Out)) { Write-Error "Publish output not found: $Out"; exit 1 }

# Optionally install Inno Setup via choco
if ($InstallInno) {
  Write-Host "Ensuring Inno Setup is installed (choco)..."
  choco install innosetup -y
}

# Locate ISCC
$possible = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$ISCC = $possible | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-Not $ISCC) { Write-Error "ISCC.exe not found. Install Inno Setup or set path."; exit 1 }

# Build installer
$issPath = "build\installer\sideprompter.iss"
if (-Not (Test-Path $issPath)) { Write-Error "ISS file not found: $issPath"; exit 1 }
Write-Host "Running ISCC: $ISCC $issPath"
& $ISCC $issPath
if ($LASTEXITCODE -ne 0) { Write-Error "ISCC failed"; exit $LASTEXITCODE }

# Find produced installer
$installer = Get-ChildItem -Path "artifacts" -Filter "*.exe" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-Not $installer) { Write-Error "No installer .exe found in artifacts"; exit 1 }

# Optional signing
if ($SignInstaller) {
  # If PFX not provided via path, try reading from environment secret SIGNING_PFX_BASE64
  $tempPfx = $null
  if (-not $PfxPath -or $PfxPath -eq "") {
    if ($env:SIGNING_PFX_BASE64) {
      Write-Host "Writing PFX from SIGNING_PFX_BASE64 to temp file"
      $tempPfx = Join-Path $env:TEMP "signing.pfx"
      [System.IO.File]::WriteAllBytes($tempPfx, [System.Convert]::FromBase64String($env:SIGNING_PFX_BASE64))
      $PfxPath = $tempPfx
      if (-not $PfxPassword -or $PfxPassword -eq "") { $PfxPassword = $env:SIGNING_PFX_PASSWORD }
    }
  }

  if (-Not (Test-Path $PfxPath)) { Write-Error "PFX not found: $PfxPath"; if ($tempPfx) { Remove-Item $tempPfx -ErrorAction SilentlyContinue }; exit 1 }
  $signtool = "signtool"
  Write-Host "Signing installer: $($installer.FullName)"
  & $signtool sign /f $PfxPath /p $PfxPassword /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $installer.FullName
  if ($LASTEXITCODE -ne 0) { Write-Error "signtool failed"; if ($tempPfx) { Remove-Item $tempPfx -ErrorAction SilentlyContinue }; exit $LASTEXITCODE }

  if ($tempPfx) {
    Remove-Item $tempPfx -Force -ErrorAction SilentlyContinue
  }
}

Write-Host "Installer ready: $($installer.FullName)"