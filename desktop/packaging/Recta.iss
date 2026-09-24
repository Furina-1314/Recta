#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif
#ifndef PayloadDir
  #error PayloadDir must point to the self-contained application payload
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
[Setup]
AppId={{7E2A1C93-6B44-4F5A-9D18-2C51E8A0F3B7}
AppName=Recta 矩衡
AppVersion={#AppVersion}
AppPublisher=Recta
AppPublisherURL=https://github.com/Furina-1314/Recta
DefaultDirName={localappdata}\Programs\Recta
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=Recta-v{#AppVersion}-win-x64-Setup
SetupIconFile={#PayloadDir}\Assets\Recta.ico
UninstallDisplayIcon={app}\Recta.App.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Recta 矩衡 (EXE)
VersionInfoVersion={#AppVersion}.0
[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,AppxManifest.xml,AppxBlockMap.xml,AppxSignature.p7x,[Content_Types].xml"
[Icons]
Name: "{userprograms}\Recta 矩衡"; Filename: "{app}\Recta.App.exe"
Name: "{userdesktop}\Recta 矩衡"; Filename: "{app}\Recta.App.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Recta.App.exe"; Description: "启动 Recta 矩衡"; Flags: nowait postinstall skipifsilent
