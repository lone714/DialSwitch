; 旋鈕切換 安裝程式（Inno Setup 6）
; 由 build.ps1 呼叫：ISCC /DMyAppVersion=x.y.z setup.iss
; 安裝到使用者自己的資料夾，不需要系統管理員權限。

#define MyAppName "旋鈕切換"
#define MyAppExe "DialSwitch.exe"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

[Setup]
; AppId 固定不變，升級時才會覆蓋舊版而不是裝第二份
AppId={{9F0C3E52-6B7A-4F0E-9B1D-3C7E5A2D8F41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=david
AppCopyright=Copyright (c) 2026 david
LicenseFile=..\LICENSE
VersionInfoVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\DialSwitch
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=DialSwitch-Setup-{#MyAppVersion}
SetupIconFile=..\src\DialSwitch\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 程式執行中時，安裝 / 解除安裝前會請使用者關閉（與程式內的 Mutex 名稱相同）
AppMutex=DialSwitch.SingleInstance
CloseApplications=force
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

[Tasks]
Name: "autostart"; Description: "登入 Windows 時自動啟動"; Flags: checkedonce
Name: "setupwheel"; Description: "自動設定 Windows 滾輪（工具 1 設為自訂工具並填入快速鍵，會先備份原本設定）"; Flags: checkedonce

[Files]
Source: "..\dist\publish\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\publish\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\publish\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\publish\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DialSwitch"; \
  ValueData: """{app}\{#MyAppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExe}"; Parameters: "--setup-wheel"; Flags: runhidden waituntilterminated; Tasks: setupwheel
Filename: "{app}\{#MyAppExe}"; Description: "立即啟動 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExe} /F"; Flags: runhidden; RunOnceId: "StopApp"
; 如果曾自動設定 Windows 滾輪，還原成安裝前的設定（沒有備份時什麼都不做）
Filename: "{app}\{#MyAppExe}"; Parameters: "--restore-wheel"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreWheel"

[Code]
// 使用者也可能是在程式設定裡開啟自動啟動，解除安裝時一律清掉。
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DialSwitch');
end;
