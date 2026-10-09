# 建置並安裝到 %LOCALAPPDATA%\Programs\DialSwitch，設定開機自動啟動，然後啟動程式。
# 用法：在專案根目錄執行  powershell -ExecutionPolicy Bypass -File .\install.ps1
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\DialSwitch\DialSwitch.csproj'
$target  = Join-Path $env:LOCALAPPDATA 'Programs\DialSwitch'
$exe     = Join-Path $target 'DialSwitch.exe'

# 先關掉執行中的版本，否則檔案被鎖住無法覆蓋。
Get-Process DialSwitch -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# 改名前的測試版（AudioOutputSwitcher）：關掉並移除它的開機啟動與捷徑，避免兩個版本同時執行。
Get-Process AudioOutputSwitcher -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'AudioOutputSwitcher' -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\音訊輸出切換器.lnk') -ErrorAction SilentlyContinue

dotnet publish $project -c Release -o $target --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "建置失敗" }

# 開始功能表捷徑：關掉之後可以直接搜尋「旋鈕切換」重新開啟。
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut(
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\旋鈕切換.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.Save()

Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'DialSwitch' -Value "`"$exe`""

# 透過檔案總管啟動，讓程式不屬於這個 PowerShell 視窗，關掉視窗也不會跟著結束。
explorer.exe $exe
Write-Host "已安裝到 $target，並設定為開機自動啟動。"
