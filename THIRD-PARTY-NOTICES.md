# 第三方聲明（Third-Party Notices）

本專案的原始碼、程式圖示（`tools/make-icon.ps1` 以向量繪製產生）與文件由 david 撰寫，以 [MIT 授權](LICENSE) 釋出。
原始碼本身沒有引用任何第三方套件或程式碼。以下是發佈檔案中包含或使用到的第三方元件。

## 隨發佈檔案一起散布

### .NET Runtime 與 Windows Desktop Runtime（WPF、Windows Forms）

安裝程式與免安裝版是「自帶 runtime」的單一執行檔，內含 Microsoft .NET Runtime 與 Windows Desktop Runtime。

- 授權：MIT License，Copyright (c) .NET Foundation and Contributors
- 原始碼：<https://github.com/dotnet/runtime>、<https://github.com/dotnet/wpf>、<https://github.com/dotnet/winforms>
- 完整授權條文與其所含的第三方聲明，隨發佈檔案附在 `licenses/` 資料夾：
  - `dotnet-LICENSE.txt`
  - `dotnet-THIRD-PARTY-NOTICES.txt`
  - `windowsdesktop-LICENSE.txt`

### Inno Setup（僅安裝程式）

安裝程式 `DialSwitch-Setup-*.exe` 以 [Inno Setup](https://jrsoftware.org/isinfo.php) 產生，安裝程式外殼包含 Inno Setup 的程式碼。
Inno Setup 授權允許自由散布以它建立的安裝程式（包含商業用途）：<https://jrsoftware.org/files/is/license.txt>
安裝腳本 `installer/setup.iss` 為本專案撰寫，屬 MIT 授權。

## 只在執行時使用、不隨程式散布

- **Segoe Fluent Icons / Segoe MDL2 Assets、Microsoft JhengHei UI 字型**：Windows 內建字型，程式只在執行時呼叫系統已安裝的字型顯示圖示與文字，不包含、不散布字型檔。
- **Windows Core Audio API、`IPolicyConfig` 介面、Windows 滾輪（RadialController）設定資料**：屬 Windows 系統元件。程式中的介面宣告與資料格式說明，是為了與 Windows 互通而依公開資訊與實際觀察自行撰寫，不包含 Microsoft 的原始碼或標頭檔。

## 商標

Microsoft、Windows、Surface Dial、Discord、Microsoft Teams、LINE 等名稱為其各自所有者的商標，本專案文件僅為描述相容性而提及，與這些公司沒有任何關聯，也未獲其背書。
