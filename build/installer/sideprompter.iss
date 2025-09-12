[Setup]
AppName=SidePrompter
AppVersion=0.1.0
DefaultDirName={pf}\SidePrompter
DefaultGroupName=SidePrompter
OutputDir=artifacts
OutputBaseFilename=SidePrompter-Setup-0.1.0
Compression=lzma
SolidCompression=yes
LicenseFile=
DisableProgramGroupPage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SidePrompter"; Filename: "{app}\SidePrompter.exe"

[Run]
Filename: "{app}\SidePrompter.exe"; Description: "Launch SidePrompter"; Flags: nowait postinstall skipifsilent