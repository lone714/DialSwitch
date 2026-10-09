# 建置發佈檔案到 dist\：
#   DialSwitch-Setup-<版本>.exe   安裝程式（需要 Inno Setup 6）
#   DialSwitch-<版本>-portable.zip 免安裝版
#   SHA256SUMS.txt                         檔案雜湊，讓下載的人可以驗證檔案沒被竄改
# 版本號只有一個來源：src\DialSwitch\DialSwitch.csproj 的 <Version>。
# 用法：powershell -ExecutionPolicy Bypass -File .\build.ps1
$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$project = Join-Path $root 'src\DialSwitch\DialSwitch.csproj'
$dist    = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'

$version = ([xml](Get-Content $project -Raw -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "csproj 的 <Version> 不是 x.y.z 格式：$version" }
Write-Host "版本 $version"

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

# 自帶 .NET runtime 的單一執行檔：對方不用另外安裝 .NET。
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish --nologo -v q `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw '建置失敗' }

# 授權檔：本專案 MIT 授權，以及單一執行檔內含的 .NET runtime 授權與第三方聲明（MIT 要求隨附）
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $publish 'LICENSE.txt')
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $publish
$licenses = New-Item -ItemType Directory (Join-Path $publish 'licenses')
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
function Find-RuntimePack($name) {
    Get-ChildItem (Join-Path $nuget $name) -Directory | Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
}
$core = Find-RuntimePack 'microsoft.netcore.app.runtime.win-x64'
$desktop = Find-RuntimePack 'microsoft.windowsdesktop.app.runtime.win-x64'
Copy-Item (Join-Path $core.FullName 'LICENSE.TXT') (Join-Path $licenses 'dotnet-LICENSE.txt')
Copy-Item (Join-Path $core.FullName 'THIRD-PARTY-NOTICES.TXT') (Join-Path $licenses 'dotnet-THIRD-PARTY-NOTICES.txt')
Copy-Item (Join-Path $desktop.FullName 'LICENSE') (Join-Path $licenses 'windowsdesktop-LICENSE.txt')

# 免安裝版
$zip = Join-Path $dist "DialSwitch-$version-portable.zip"
Compress-Archive -DestinationPath $zip -Path (Join-Path $publish 'DialSwitch.exe'), (Join-Path $publish 'LICENSE.txt'),
    (Join-Path $publish 'THIRD-PARTY-NOTICES.md'), $licenses.FullName, (Join-Path $root 'README.md'), (Join-Path $root 'CHANGELOG.md')

# 安裝程式
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc /Q "/DMyAppVersion=$version" (Join-Path $root 'installer\setup.iss')
    if ($LASTEXITCODE -ne 0) { throw '安裝程式建置失敗' }
} else {
    Write-Warning '找不到 Inno Setup 6，略過安裝程式（winget install JRSoftware.InnoSetup）'
}

# 雜湊
Get-ChildItem $dist -File | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)"
} | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii

Get-ChildItem $dist -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
