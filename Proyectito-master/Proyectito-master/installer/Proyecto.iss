; Proyecto -Instalador (Windows x64, autosuficiente)
#define Nombre "Proyecto"
#define Version "1.3.2"
#define Editor "Proyecto"
#define PublicadoDir "..\publish\sc"

[Setup]
AppId={{B9A5FE24-3C71-4E8A-9A12-5D6C1B7E0F24}
AppName={#Nombre}
AppVersion={#Version}
AppPublisher={#Editor}
DefaultDirName={localappdata}\Programs\{#Nombre}
DefaultGroupName={#Nombre}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Proyecto.exe
SetupIconFile={#PublicadoDir}\logo.ico
OutputDir=.
OutputBaseFilename=Proyecto-Setup_v1.3.2
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
AppVerName={#Nombre} {#Version}

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "{#PublicadoDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#Nombre}"; Filename: "{app}\Proyecto.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#Nombre}"; Filename: "{app}\Proyecto.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Proyecto.exe"; Description: "Abrir {#Nombre} ahora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v Proyecto /f"; Flags: runhidden