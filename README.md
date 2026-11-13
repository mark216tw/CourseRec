# CourseRec — Windows 線上課程／會議錄影與快剪工作站

`CourseRec` 是一款專為「線上課程複習」與「遠距視訊會議」打造的輕量級 Windows 桌面錄影與快剪工具。全程採用 **純 Windows 原生影音管線**（`Windows.Graphics.Capture`、WASAPI / Process Loopback、Media Foundation `IMFSinkWriter`、`Windows.Media.Editing`），免外掛 FFmpeg，兼顧高穩定性、低硬碟佔用與課後快速複習體驗。

## 核心特色

- **情境模式與即時雙軌音量監控**
  - 純聽課模式自動開啟課程聲音並靜音麥克風，避免環境雜音與鍵盤聲。
  - 參與會議模式同時擷取課程與麥克風，內建 48 kHz 雙軌混音與 0.85 Soft Limiter 防爆音保護。
  - 未開錄即可看到系統與麥克風雙音量表，並顯示實際錄音來源與麥克風狀態。
- **背景視窗鎖定與單一視窗隔離錄音**
  - 支援全螢幕（多螢幕）或鎖定指定視窗，上層筆記軟體不影響錄影。
  - Windows 10 Build 20348+ / Windows 11 支援 Process Loopback，只錄目標視窗聲音；舊版自動降級為系統全體聲音。
  - 目標視窗最小化時自動重送最後一幀，維持影音同步並主動提示。
- **省空間硬體編碼與錄影中重點標記**
  - 提供「簡報／會議省空間（1080p 上限、15 fps、H.264 High Profile）」與「標準流暢（原生偶數解析度、30 fps）」預設。
  - 支援 0～30 秒倒數、暫停／繼續、定時自動停止、低磁碟空間保護、時間書籤、PNG 快拍與防入鏡迷你懸浮控制列。
  - 主視窗記住位置、大小與最大化狀態；提供 8 種顯示主題。
- **課後快剪與 AI 逐字稿音訊匯出**
  - 內建 0.25x～2.0x 倍速播放、播放器全螢幕與書籤一鍵跳轉。
  - 支援頭尾修剪、多段中場休息刪除與人聲音量增強；匯出時自動校正剩餘書籤時間點。
  - 可匯出剪輯後 MP4，或一鍵匯出 48 kHz／Mono／96 kbps 的 M4A 純音訊檔，供 NotebookLM、Whisper 等 AI 摘要工具使用。
- **檔案庫與操作回饋**
  - 即時檔名／書籤備註搜尋、排序、合計大小與時長、重新命名、顯示位置與刪除。
  - 全域快捷鍵、工作列錄影指示、分級通知、重大錯誤可複製診斷資訊。
  - 錄影期間自動阻止系統休眠與關閉螢幕。

## 系統需求

| 項目 | 需求 |
| :--- | :--- |
| 作業系統 | Windows 10 1809（Build 17763）以上 |
| 架構 | x64 或 ARM64（請下載對應的 Release 壓縮檔） |
| 執行階段 | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（非 Self-Contained 版本必須） |
| 影音管線 | Windows App SDK 1.8 已隨附於發行檔，無需另裝 |
| 進階功能 | 「只錄單一視窗聲音」需 Windows 10 Build 20348 或 Windows 11 以上 |

> Windows 10 1809 的 x64 版本可正常錄影，但 Process Loopback 會自動降級為系統全體聲音。

## 下載與安裝

本專案**不提供安裝程式**，請由 [GitHub Releases](https://github.com/mark216tw/CourseRec/releases) 下載對應架構的 zip：

- `CourseRec-<版本>-win-x64-framework-dependent.zip`
- `CourseRec-<版本>-win-arm64-framework-dependent.zip`

安裝步驟：

1. 安裝 **.NET 10 Desktop Runtime**（x64 或 ARM64 對應版本）。
2. 將 zip 解壓縮到任意資料夾（例如 `C:\Apps\CourseRec`），請勿直接在壓縮檔內執行。
3. 執行 `Com.VibeCode.CourseRec.exe`。
4. 若出現 SmartScreen 提示，請選擇「更多資訊」→「仍要執行」。

解除安裝：刪除解壓縮資料夾即可；個人設定與日誌可另行刪除 `%AppData%\CourseRec`。

## 快速開始

1. 開啟程式後進入「錄影工作台」，選擇「純聽課」或「參與會議」情境。
2. 選擇錄影來源：全螢幕或指定視窗；需要隔離通知音時開啟「只錄此視窗聲音」。
3. 確認雙音量表有跳動，設定倒數與畫質後按「開始錄影」。
4. 錄影中可使用全域快捷鍵新增書籤或快拍，或切換到迷你懸浮控制列。
5. 停止後至「檔案庫」開啟「快剪與複習」，修剪後匯出 MP4 或 M4A。

## 預設全域快捷鍵

| 功能 | 預設快捷鍵 |
| :--- | :--- |
| 開始／停止錄影 | `Ctrl+Shift+R` |
| 暫停／繼續錄影 | `Ctrl+Shift+P` |
| 新增重點時間書籤 | `Ctrl+Shift+B` |
| 投影片 PNG 快拍 | `Ctrl+Shift+S` |

快捷鍵可在設定頁按鍵捕捉重新錄製，或一鍵回復預設值。

## 資料儲存位置

| 內容 | 路徑 |
| :--- | :--- |
| 個人設定 | `%AppData%\CourseRec\settings.json` |
| 執行日誌 | `%AppData%\CourseRec\logs\courserec.log` |
| 預設錄影輸出 | 由設定頁指定，預設為使用者「影片」資料夾下的 `CourseRec` |
| 書籤 | 與錄影檔同名的 `.bookmarks.json` |
| 快拍 | 錄影檔旁的 `Snapshots` 資料夾 |

日誌以有界背景佇列批次寫入，重複訊息會彙總；達 5 MB 後輪替至 `.prev`，正常關閉時排空佇列。

## 專案結構

```text
src/CourseRec/                 # WinUI 3 應用程式
  Views/                       # 主視窗、錄影、檔案庫、快剪、設定、關於、懸浮列
  ViewModels/                  # MVVM ViewModel
  Models/                      # 設定、書籤、擷取目標、錄影狀態等模型
  Services/                    # Capture／Audio／Encoding／Editing／Hotkeys／Bookmarks／Settings
  Interop/                     # Win32、D3D11、WASAPI、Media Foundation P/Invoke
  Helpers/                     # 轉換器、日誌、檔案與格式輔助
  Strings/zh-Hant/             # 繁體中文介面字串
  Styles/                      # 共用樣式與主題資源字典
tests/CourseRec.Tests/         # xUnit 單元測試
docs/                          # 使用指南、架構、設計、建置與開發紀錄
```

## 建置與測試

需安裝 **.NET 10 SDK**，於專案根目錄執行：

```powershell
# 建置（Debug x64）
dotnet build CourseRec.sln

# 單元測試
dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj

# 格式檢查
dotnet format CourseRec.sln --verify-no-changes --no-restore
```

非 Self-Contained 發行（需目標機器安裝 .NET 10 Desktop Runtime）：

```powershell
# x64
dotnet publish src/CourseRec/CourseRec.csproj -c Release -r win-x64 --self-contained false `
  -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None `
  -o artifacts/publish/win-x64

# ARM64
dotnet publish src/CourseRec/CourseRec.csproj -c Release -r win-arm64 --self-contained false `
  -p:Platform=ARM64 -p:PlatformTarget=ARM64 -p:WindowsAppSDKSelfContained=true `
  -p:WindowsPackageType=None -o artifacts/publish/win-arm64
```

完整建置、封裝與發行流程請見 [docs/BUILD.md](docs/BUILD.md)。

## 文件

- [使用指南](docs/USER_GUIDE.md)
- [系統架構與技術文件](docs/ARCHITECTURE.md)
- [系統設計文件](docs/DESIGN.md)
- [建置與發行](docs/BUILD.md)
- [實作計畫](docs/IMPLEMENTATION_PLAN.md)
- [開發紀錄](docs/DEV_LOG.md)
- [版本異動](CHANGELOG.md)／[發行說明](RELEASE_NOTES.md)

## 授權

本專案採用 [MIT License](LICENSE)，Copyright (c) 2026 mark216tw。
