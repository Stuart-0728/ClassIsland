#define AppName "巴蜀作业悬浮窗测试版"
#define AppExeName "ClassIsland.Desktop.exe"
[Setup]
AppId={{4C387517-7940-4B38-9A79-6101ABE530DA}}
AppName={#AppName}
AppVersion=2.2.12
DefaultDirName={localappdata}\BashuHomeworkPreview
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
LicenseFile={#WorkspaceDir}\LICENSE.txt
SetupIconFile={#WorkspaceDir}\ClassIsland\Assets\AppLogo.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
#endif
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
[Run]
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "打开平台作业同步测试版"; Flags: nowait postinstall skipifsilent
