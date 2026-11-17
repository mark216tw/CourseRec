# 開發紀錄

最新紀錄放最上面。每次開發工作都要更新，不要事後補寫。

---

## 2026-11-17 - v1.5.5

### 目標
修正快剪與複習編輯器「全螢幕」按鈕按下後沒有反應的問題。

### 變更
- `Views/EditorPage.xaml`：播放器外框命名為 `PlayerHostBorder`；新增全螢幕覆蓋層 `PlayerFullScreenHost`（含「退出全螢幕」按鈕與 `Esc` 鍵快速鍵）；「全螢幕」按鈕改為未載入影片時停用。
- `Views/EditorPage.xaml.cs`：移除無效的 `MediaPlayerElement.IsFullWindow` 切換，改以 `AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen)` 進入視窗全螢幕，並將播放器移入覆蓋層；退出時歸位、還原最大化狀態與導覽列；`Esc` 與頁面卸載都會自動退出。
- `Views/MainWindow.xaml.cs`：新增 `SetNavigationPaneVisible`，全螢幕時隱藏側邊導覽列。
- `Strings/zh-Hant/Resources.resw`：新增退出全螢幕按鈕文字、無障礙名稱與工具提示。
- `Directory.Build.props`、`CHANGELOG.md`、`RELEASE_NOTES.md`、`docs/BUILD.md`、`docs/USER_GUIDE.md`：版本更新至 1.5.5（`AssemblyVersion` 維持 1.5.0.0）。

### 問題與解法
- `MediaPlayerElement.IsFullWindow` 依賴 UWP 的 `CoreWindow`／`ApplicationView`，在 WinUI 3 桌面應用不支援且不會拋錯，因此按下按鈕完全沒有反應。
- 改為調整 `AppWindow` 簡報器並把播放器移到頁面覆蓋層；因覆蓋層位於 `Frame` 內，另需隱藏 `NavigationView` 的側邊面板才能讓影片填滿整個視窗。
- WinUI 3 的 `MediaTransportControls` 已無 `IsFullWindowButtonVisible`／`IsFullWindowEnabled`，無法直接隱藏內建全螢幕鈕，維持原樣。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。

### 待辦
- 尚未人工確認全螢幕切換時的畫面重整、退出後最大化還原，以及多螢幕環境下的顯示位置。

---

## 2026-11-17 - v1.5.4

### 目標
在快剪與複習編輯器的播放區塊加入可視化剪輯標記時間軸，讓使用者設定頭尾保留、剪除區間、待設定剪除點與書籤時，能直接在時間軸上看到對應位置並點擊／拖曳跳轉。

### 變更
- `Controls/EditorTimeline.xaml`、`Controls/EditorTimeline.xaml.cs`：新增剪輯標記時間軸控制項，繪製保留範圍、剪除片段、待設定剪除點、書籤與播放頭；支援點擊／拖曳跳轉、標記工具提示與主題筆刷。
- `Views/EditorPage.xaml`：播放器下方加入時間軸與四項圖例（保留範圍、剪除片段、待設定剪除點、書籤）。
- `Views/EditorPage.xaml.cs`：訂閱 ViewModel 屬性與集合變更以重繪標記；播放位置變更時以 50 ms 節流更新播放頭；時間軸跳轉直接設定播放器位置；視窗載入／卸載時正確訂閱與取消訂閱。
- `ViewModels/EditorViewModel.cs`：公開唯讀 `PendingCutStart`，並在標記、加入片段、重設、載入與卸載時通知變更；新增 `GetTimelineToolTipFormats()` 取得本地化提示格式。
- `Strings/zh-Hant/Resources.resw`：新增時間軸無障礙名稱、圖例與各標記工具提示格式字串。
- `tests/CourseRec.Tests/EditorAndLibraryTests.cs`：補充 `PendingCutStart` 標記、加入片段與重設後狀態的斷言。
- `Directory.Build.props`、`CHANGELOG.md`、`RELEASE_NOTES.md`、`docs/BUILD.md`、`docs/USER_GUIDE.md`：版本更新至 1.5.4（`AssemblyVersion` 維持 1.5.0.0），補上時間軸使用說明。

### 問題與解法
- 內建 `MediaTransportControls` 的進度列樣板不開放穩定插入標記，因此改在播放器下方自繪時間軸，不動內建播放控制。
- 動態建立的標記無法直接使用 `ThemeResource`，改由隱藏的筆刷來源元素取得主題筆刷，並於 `ActualThemeChanged` 時重繪。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：63/63 通過。

### 待辦
- 尚未人工確認時間軸在淺色／深色主題、不同視窗寬度與觸控拖曳下的標記位置與操作手感。

---

## 2026-11-14 - v1.5.3

### 目標
讓主題顏色按鈕顯示的選取色塊與選單中的色塊大小一致。

### 變更
- `Views/SettingsPage.xaml`：移除按鈕專用的 64x28 色塊樣板，改用與選單相同的 44x24 `ColorThemeSwatchTemplate`。
- `Directory.Build.props`、`CHANGELOG.md`、`RELEASE_NOTES.md`：版本更新至 1.5.3（`AssemblyVersion` 維持 1.5.0.0）。
- `docs/BUILD.md`：發行與 Release 範例版本更新為 1.5.3，`AssemblyVersion` 範例同步為 1.5.0.0。
- `docs/USER_GUIDE.md`：色彩主題說明補齊 10 種主題名稱。

### 問題與解法
- 按鈕色塊原本使用獨立樣板，尺寸比選單色塊大；改為共用同一樣板後即可保持一致。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：63/63 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：無格式差異（僅出現工作區載入警告）。

### 待辦
- 尚未人工確認按鈕與選單色塊在淺色／深色下大小與位置一致。

---

## 2026-11-14 - v1.5.2

### 目標
修正主題顏色色塊在選單中靠下、未置中的問題。

### 變更
- `Views/SettingsPage.xaml`：為 `GridView` 加入 `ItemContainerStyle`，覆寫預設 `MinHeight`、`Margin` 並將內容水平／垂直置中；色塊本身也加上 `HorizontalAlignment`／`VerticalAlignment`。
- `Directory.Build.props`、`CHANGELOG.md`、`RELEASE_NOTES.md`：版本更新至 1.5.2（`AssemblyVersion` 維持 1.5.0.0）。

### 問題與解法
- `GridViewItem` 預設 `MinHeight` 大於 `ItemsWrapGrid.ItemHeight`，且預設留有邊距，導致色塊被推到欄位下方。改以 `DefaultGridViewItemStyle` 為基礎覆寫尺寸與對齊後恢復置中。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：63/63 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：無格式差異（僅出現工作區載入警告）。

### 待辦
- 尚未人工確認色塊在淺色／深色下皆完全置中。

---

## 2026-11-14 - v1.5.1

### 目標
精簡主題顏色面板，讓 10 種顏色一次顯示且不出現卷軸。

### 變更
- `Views/SettingsPage.xaml`：色塊縮小為 44x24，按鈕色塊為 64x28；面板改為 5 欄 x 2 列，關閉水平與垂直捲動，並縮小面板間距與寬度。
- `Directory.Build.props`、`CHANGELOG.md`、`RELEASE_NOTES.md`：版本更新至 1.5.1（`AssemblyVersion` 維持 1.5.0.0）。

### 問題與解法
- 無。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：63/63 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：無格式差異（僅出現工作區載入警告）。

### 待辦
- 尚未人工確認面板在淺色／深色下是否完整顯示 10 種顏色且無卷軸。

---

## 2026-11-14 - v1.5.0

### 目標
優化相近的主題配色，並將主題顏色下拉選單改為飛出式色塊面板。

### 變更
- `Models/AppColorTheme.cs`：新增 `Indigo`，`Sky` 標記為已停用並保留舊設定讀取相容。
- `Services/Settings/SettingsService.cs`：載入舊設定時將 `Sky` 自動轉為 `Indigo`。
- `Services/Settings/ThemeService.cs`：以 `IndigoTheme.xaml` 取代 `SkyTheme.xaml`。
- `Styles/Themes/`：新增 `IndigoTheme.xaml`，移除 `SkyTheme.xaml`；調整 `OceanTheme.xaml`、`SunsetTheme.xaml`、`CocoaTheme.xaml` 的淺色與深色配色。
- `Models/ColorThemeOption.cs`、`ViewModels/SettingsViewModel.cs`：色塊改為單一強調色，並更新 10 種主題的強調色。
- `Views/SettingsPage.xaml`：以「目前色塊按鈕 + `Flyout`」取代 ComboBox；面板內以 `GridView` 顯示色塊，點選後立即套用並關閉。
- `Views/SettingsPage.xaml.cs`：新增 `OnColorThemeItemClick` 關閉飛出面板。
- `Strings/zh-Hant/Resources.resw`：新增主題顏色按鈕、飛出面板與色塊清單字串，將天空藍改為靛藍。
- `tests/CourseRec.Tests/SettingsServiceTests.cs`：新增天空藍遷移為靛藍的測試。
- `Directory.Build.props`、`CHANGELOG.md`、`RELEASE_NOTES.md`：版本更新至 1.5.0。

### 問題與解法
- 驗證一般輸出建置時 `Com.VibeCode.CourseRec.exe` 曾由執行中的程式鎖定，先以 `-p:BaseOutputPath` 輸出至暫存目錄完成建置；程式關閉後已回到一般輸出位置重新建置成功。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：63/63 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：無格式差異（僅出現工作區載入警告）。

### 待辦
- 尚未人工檢視飛出式色塊面板在淺色／深色下的外觀、選取狀態與 10 種主題實際套用效果。

---

## 2026-11-14 - v1.4.1

### 目標
將主題顏色下拉選單改為色塊選單，選項不顯示顏色名稱。

### 變更
- `Models/ColorThemeOption.cs`：新增主題顏色選項，包含選單顯示文字、主題背景色與強調色。
- `ViewModels/SettingsViewModel.cs`：主題顏色選項改為 `ColorThemeOption`，並提供 10 組色塊配色。
- `Views/SettingsPage.xaml`：`CmbColorTheme` 加入 `ItemTemplate`，以背景色到強調色的漸層色塊呈現；名稱僅保留於 `AutomationProperties.Name` 供螢幕閱讀器使用。
- `Directory.Build.props`、`CHANGELOG.md`、`RELEASE_NOTES.md`：版本更新至 1.4.1（`AssemblyVersion` 維持 1.4.0.0）。

### 問題與解法
- 驗證建置時 `Com.VibeCode.CourseRec.exe` 正由執行中的程式鎖定，先改用 `-p:BaseOutputPath` 輸出至暫存目錄完成建置；程式關閉後已回到一般輸出位置重新建置成功。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：62/62 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：無格式差異（僅出現工作區載入警告）。

### 待辦
- 尚未人工檢視色塊選單在淺色／深色下的外觀與選取狀態。

---

## 2026-11-14 - v1.4.0

### 目標
擴充主題顏色選項，新增 5 種配色。

### 變更
- `Styles/Themes/`：新增 `RoseTheme.xaml`、`MintTheme.xaml`、`GraphiteTheme.xaml`、`CocoaTheme.xaml`、`SkyTheme.xaml`，皆含淺色、深色與 Default 色階。
- `Models/AppColorTheme.cs`：新增 `Rose`、`Mint`、`Graphite`、`Cocoa`、`Sky`。
- `Services/Settings/ThemeService.cs`：註冊 5 組新主題資源字典。
- `ViewModels/SettingsViewModel.cs`：設定頁主題顏色清單新增 5 個選項。
- `Strings/zh-Hant/Resources.resw`：新增主題名稱並更新主題選單的無障礙說明。
- `README.md`、`CHANGELOG.md`、`Directory.Build.props`：更新主題數量與版本至 1.4.0（`AssemblyVersion` 1.4.0.0）。

### 問題與解法
- 無。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：62/62 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：無格式差異（僅出現工作區載入警告）。

### 待辦
- 尚未人工檢視 10 種主題在淺色／深色下的實際對比度與迷你控制列外觀。

---

## 2026-11-14 - v1.3.8

### 目標
停止錄影回到待命狀態時，迷你控制列顯示的錄影時間要歸零。

### 變更
- `ViewModels/RecorderViewModel.cs`：`OnStateChanged` 進入 `RecordingState.Idle` 時同步將 `ElapsedTimeText` 設回 `00:00:00`。
- 更新版本至 1.3.8（`AssemblyVersion` 維持 1.3.0.0）。

### 問題與解法
- 停止錄影的 `finally` 只重設時間軸並切回待命，未同步更新顯示文字，因此迷你控制列保留最後一次的錄影時間。改在狀態切換集中處理，停止錄影、取消倒數與啟動失敗等回到待命的路徑都會歸零。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：62/62 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過。

### 待辦
- 尚未人工驗證停止錄影後迷你控制列顯示 `00:00:00`。

---

## 2026-11-14 - v1.3.7

### 目標
開啟迷你控制列後讓焦點保留在迷你控制列，方便直接操作列上按鈕。

### 變更
- `Services/MiniOverlayService.cs`：移除 `Show()` 啟用迷你控制列後把焦點交還主視窗的 `BringToFront()`，改為由迷你控制列取得焦點。
- 更新版本至 1.3.7（`AssemblyVersion` 維持 1.3.0.0）。

### 問題與解法
- v1.3.5 為避免主視窗失去焦點造成視覺變化，曾在顯示後把焦點交還主視窗；此行為會讓使用者開啟控制列後仍需再點一下才能操作。閃爍已於 v1.3.6 從顯示前預先設定大小與位置解決，因此可安全移除焦點歸還邏輯。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：62/62 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過。

### 待辦
- 尚未人工驗證開啟迷你控制列後焦點確實在控制列，且主視窗不再閃爍。

---

## 2026-11-14 - v1.3.6

### 目標
修正迷你控制列開啟時先以預設大尺寸顯示、再縮小並移動所造成的畫面閃爍。

### 變更
- `Models/MiniOverlayPosition.cs`：新增 `WidthDip`、`HeightDip`（預設 0），以 DIP 記錄控制列大小。
- `Services/Settings/SettingsService.cs`：新增 `NormalizeMiniOverlayPosition`，將寬高夾限在合理範圍，舊設定缺少寬高時維持 0。
- `Interop/WindowNativeMethods.cs`：新增 `GetDpiForWindow` P/Invoke，用於顯示前取得目標螢幕縮放比例。
- `Views/MiniOverlayWindow.xaml.cs`：新增 `PrepareInitialBounds`，在 `Activate` 前先移到上次位置、依 DPI 換算上次 DIP 大小（或預先量測／預設值）並 `ResizeClient`，再夾回可見工作區；`RootBorder` 在 `Loaded` 完成尺寸與位置校正前保持 `Opacity=0`，完成後才顯示。
- `Views/MiniOverlayWindow.xaml.cs`：`PersistPositionAsync` 一併儲存 `ClientSize` 換算後的 DIP 寬高；拖曳與關閉時都會保存。
- `tests/CourseRec.Tests/SettingsServiceTests.cs`：新增 DIP 寬高儲存與讀取的 round-trip 測試。
- 更新版本至 1.3.6（`AssemblyVersion` 維持 1.3.0.0）。

### 問題與解法
- v1.3.5 只把焦點交還主視窗，未處理迷你控制列第一幀的尺寸。WinUI 視窗在 `Activate` 前若未設定大小，會先以預設較大尺寸呈現，直到 `Loaded` 才 `ResizeClient` 成內容大小，因此仍會看到「大變小」的閃爍。
- 改為在視窗建構子就先 `Move` 與 `ResizeClient`，並以儲存的 DIP 大小或預先量測結果決定尺寸；即使量測失敗改用預設值，`Loaded` 前的內容也維持透明，不會讓使用者看到中間尺寸。
- 大小以 DIP 儲存而非實體像素，避免 DPI 不同的螢幕還原時尺寸不一致。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：62/62 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過。

### 待辦
- 尚未人工驗證連續切換迷你控制列不再由大變小、舊版 `settings.json` 相容性，以及 100%／150% DPI 下的顯示大小。

---

## 2026-11-14 - v1.3.5

### 目標
修正從錄影工作台開啟迷你控制列時主視窗閃爍；取消程式啟動時自動顯示迷你控制列，但保留關閉程式時自動關閉；移除迷你控制列的「開啟主視窗」按鈕。

### 變更
- `Services/MiniOverlayService.cs`：`Show()` 啟用迷你控制列後，於同一個 UI 回合呼叫主視窗 `BringToFront()`，把焦點交還主視窗。
- `App.xaml.cs`：`OnLaunched` 不再自動顯示迷你控制列，也不再於顯示後重新啟用主視窗；`OnMainWindowClosed` 仍會關閉迷你控制列。
- `Views/MiniOverlayWindow.xaml`、`Views/MiniOverlayWindow.xaml.cs`：移除「開啟主視窗」按鈕與 `OnExpandMainWindowClicked` 事件處理。
- `Strings/zh-Hant/Resources.resw`：移除 `MiniBtnExpand` 的自動化名稱與工具提示文字。
- `README.md`、`docs/ARCHITECTURE.md`、`docs/DESIGN.md`、`docs/USER_GUIDE.md`：同步迷你控制列開啟方式與按鈕配置。
- 更新版本至 1.3.5（`AssemblyVersion` 維持 1.3.0.0）。

### 問題與解法
- 迷你控制列呼叫 `Activate()` 時會讓主視窗短暫失焦，WinUI 因而重繪非作用中的標題列與背景，形成肉眼可見的閃爍。改為在啟用迷你控制列的同一個 UI 執行緒回合內立刻把焦點交還主視窗，合成器只呈現最終狀態，避免中間的非作用中畫面。
- 先前嘗試在新視窗顯示前後調整尺寸與位置，無法解決主視窗失焦造成的閃爍，因此改從焦點切換著手。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：61/61 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過。

### 待辦
- 尚未人工驗證開啟迷你控制列時主視窗不再閃爍、焦點確實留在主視窗，以及關閉程式時迷你控制列會一起關閉。

---

## 2026-11-14 - v1.3.4

### 目標
修正從錄影檔案庫點「快剪與複習」時，編輯器已有影片卻不會切換到快剪頁的問題；補上覆蓋提示，並調整剪除、快拍與匯出相關文字與按鈕位置。

### 變更
- `Views/MainWindow.xaml.cs`：改為直接訂閱 `INavigationCoordinator.NavigationRequested`，每次導覽請求都切換頁面；手動切頁時同步 `MainViewModel.CurrentPage`。
- `Views/LibraryPage.xaml.cs`、`ViewModels/LibraryViewModel.cs`：在檔案庫開啟快剪前，若編輯器已有影片，先顯示取代確認對話框，確認後才導覽。
- `ViewModels/EditorViewModel.cs`：`LoadVideoAsync` 新增 `replacementConfirmed` 參數避免重複提示；「顯示匯出檔案」改為開啟匯出資料夾命令。
- `Views/EditorPage.xaml.cs`：頁面載入時若影片仍在載入中，不再重複嘗試還原上次影片。
- `Views/EditorPage.xaml`：將「開啟剛完成的匯出影片」與「開啟匯出目錄」移到匯出按鈕列；匯出目錄改為圖示按鈕且不需先匯出。
- `Strings/zh-Hant/Resources.resw`：調整「標記剪除終點」、「擷取當前畫面為PNG」、「開啟剛完成的匯出影片」、「匯出M4A純音訊」、「開啟匯出目錄」等文案，新增取代影片對話框與匯出資料夾錯誤字串。
- `tests/CourseRec.Tests/EditorAndLibraryTests.cs`：新增已確認取代時不再重複提示的測試。
- 版本更新至 1.3.4（`AssemblyVersion` 維持 1.3.0.0）。

### 問題與解法
- 原導覽只在 `MainViewModel.CurrentPage` 屬性變更時觸發；使用者手動切到檔案庫不會更新 `CurrentPage`，導致再次從檔案庫開啟快剪時屬性值相同而沒有導覽。改由 `MainWindow` 直接訂閱導覽協調器事件後，即使目標頁面與目前值相同也會切換。
- 檔案庫與編輯器各自可能顯示未儲存變更提示；由檔案庫統一先顯示覆蓋提示，並在協調器載入路徑標記已確認，避免連續跳出兩個對話框。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：61/61 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過。

### 待辦
- 尚未人工驗證最小視窗寬度下匯出按鈕列是否會過於擁擠。
- 尚未人工驗證檔案庫覆蓋提示取消後仍留在檔案庫，以及確認後只出現一次提示。

---

## 2026-11-14 - v1.3.3

### 目標
修正迷你控制列剛啟動時內容空白，需滑鼠點擊後才顯示「待命中」、錄影時間與音量資料的問題。

### 變更
- `Services/MiniOverlayService.cs`：顯示迷你控制列改回呼叫 `Window.Activate()`，確保 XAML 內容與 `Loaded` 事件立即完成。
- `App.xaml.cs`：自動顯示迷你控制列後再呼叫 `_mainWindow.Activate()`，將焦點還給主視窗。
- 版本更新至 1.3.3（`AssemblyVersion` 維持 1.3.0.0）。

### 問題與解法
- 前一版使用 `AppWindow.Show(activateWindow: false)` 只顯示視窗而未啟用；WinUI 3 在首次啟用前不會完成內容繪製與尺寸量測，因此狀態文字要等到點擊視窗後才出現。改回 `Activate()` 後內容會立即顯示，再由 `App` 將焦點交還主視窗。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：60/60 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過。

### 待辦
- 尚未人工驗證啟動後迷你控制列立即顯示「待命中」與 `00:00:00`，以及焦點是否留在主視窗。

---

## 2026-11-14 - v1.3.2

### 目標
將「迷你懸浮控制列」更名為「迷你控制列」，讓迷你控制列隨應用程式啟動自動顯示、關閉應用程式時自動關閉，停止錄影後保持顯示，並縮小按鈕與整體所占面積。

### 變更
- `Strings/zh-Hant/Resources.resw`：所有介面文字、標題、自動化名稱與設定說明改為「迷你控制列」。
- `Services/IMiniOverlayService.cs`、`Services/MiniOverlayService.cs`：新增迷你控制列生命週期服務，集中處理顯示、切換、關閉、主題註冊與排除擷取設定同步。
- `App.xaml.cs`：註冊 `IMiniOverlayService`；`OnLaunched` 自動顯示迷你控制列；`OnMainWindowClosed` 先關閉迷你控制列再停止 Host。
- `Views/RecorderPage.xaml.cs`：移除頁面自行建立與關閉迷你列的邏輯，切換改呼叫 `IMiniOverlayService.Toggle()`。
- `ViewModels/RecorderViewModel.cs`：移除 `CloseMiniOverlayRequested` 事件與停止錄影後自動關閉迷你列的觸發。
- `Views/MiniOverlayWindow.xaml`、`Styles/SharedStyles.xaml`：縮小外框內距、按鈕尺寸與間距、指示燈、進度環與音量條，新增 `MiniOverlayIconButtonStyle`。
- `README.md`、`docs/USER_GUIDE.md`、`docs/DESIGN.md`、`docs/ARCHITECTURE.md`、`docs/IMPLEMENTATION_PLAN.md`、`AGENTS.md`：同步新名稱與生命週期說明。
- 版本更新至 1.3.2（`AssemblyVersion` 維持 1.3.0.0）。

### 問題與解法
- 原本迷你控制列由 `RecorderPage` 建立，若啟動頁面不是錄影工作台就不會出現，且無法隨主視窗關閉；改由 Singleton 服務持有，讓它與應用程式同生命週期。
- 迷你控制列改為常駐後，若在設定頁切換「排除擷取」，原本只在建立時套用會失效；改訂閱 `SettingsChanged` 即時呼叫 `SetExcludeFromCapture`。
- WinUI 3 的 `Window` 沒有 `Resources` 屬性，按鈕樣式改放在 `Styles/SharedStyles.xaml`。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：60/60 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過。

### 待辦
- 尚未人工驗證啟動自動顯示、關閉應用程式自動關閉，以及 100%／125%／150%／200% DPI 下縮小後的迷你控制列外觀與拖曳。

---

## 2026-11-14 - v1.3.1

### 目標
縮短從雙擊到主視窗首次顯示的時間；將即時預覽與音量監控延後到第一幀後啟動，並加入啟動耗時日誌以供後續比較。依使用者決定不啟用 ReadyToRun，只調整程式流程。

### 變更
- `Helpers/StartupTrace.cs`：新增啟動階段耗時量測，透過現有 `ILogger<App>` 寫入 `courserec.log`。
- `Program.cs`、`App.xaml.cs`：標記進入點、App 建構、Host 建置／啟動、設定載入、視窗建構／Activate、閒置服務啟動等節點。
- `App.xaml.cs`：改用精簡 `HostBuilder`（僅保留 ContentRoot、`appsettings.json` 與檔案日誌），移除 `CreateDefaultBuilder` 的環境變數／命令列等額外設定來源。
- `App.OnLaunched` 重排：先建主視窗並 `Activate()`，再進行 `SettingsViewModel`、剪貼簿、工作列指示、全域快捷鍵與初始頁面導覽；移除啟動時預先解析 `SettingsViewModel`。
- `MainWindow`：建構子不再同步導覽頁面；新增 `NavigateToInitialPage`，以 `DispatcherQueuePriority.Low` 在第一幀後導覽，`ContentFrame` 先顯示 `ProgressRing` 佔位。
- `Services/Encoding/Mp4EncoderService.cs`：`MFStartup` 由建構子改為第一次 `Open()` 時延遲初始化（加鎖、僅嘗試一次），`Dispose` 僅在已初始化時 `MFShutdown`。
- `Helpers/LocalizationService.cs`：`ResourceLoader` 與 `.resw` 後備字典改為 `Lazy`，首次解析失敗時才載入。
- `ViewModels/RecorderViewModel.cs`：拆分 `AttachDispatcher` 與 `StartIdleServices`；來源枚舉、磁碟摘要與家務計時器延後啟動；`ResumeIdleCapture` 會確保閒置服務已啟動；暫停閒置擷取時不再因選取來源而重啟音訊監控。
- `Views/RecorderPage.xaml.cs`：頁面載入後以低優先權延後啟動 D3D11／WGC 預覽與 WASAPI 監控。
- `Services/Settings/ThemeService.cs`：新視窗首次套用主題時跳過「反向主題再切回」的強制刷新。
- 版本更新至 1.3.1（`AssemblyVersion` 維持 1.3.0.0）。

### 問題與解法
- 原本 `RecorderPage` 在 `MainWindow` 建構子內同步導覽，頁面 XAML 與 `MFStartup` 都在視窗可見前完成；改為 Activate 後低優先權導覽並延後 MF 初始化。
- `StartIdleServices` 觸發的來源枚舉完成後會選取第一個來源，原本會立刻啟動預覽與音訊；加上 `_isIdleCaptureSuspended` 判斷，避免非錄影頁或暫停狀態下重啟監控。
- `DispatcherQueuePriority` 在 `RecorderPage` 同時存在 `Microsoft.UI.Dispatching` 與 `Windows.System` 兩個命名空間，改用完整名稱避免模稜兩可。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：60/60 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過。
- 啟動耗時（Debug x64，暖啟動、上次頁面為錄影工作台，單位 ms）：

| 節點 | 優化前 | 優化後 |
| --- | --- | --- |
| App XAML 資源載入完成 | 123 | 117 |
| 主機建置完成 | 244 | 231 |
| 設定載入完成 | 454 | 506 |
| 主視窗建構完成 | 756 | 717 |
| 視窗 Activate | 777 | 733 |
| 啟動後初始化完成 | 781 | 745 |

- 優化後錄影頁 XAML 建構與 D3D11／WGC／WASAPI 啟動移到 `Activate` 之後，不再計入上表；實機冷啟動首跑因 JIT 與防毒掃描變異較大，未列入比較。

### 待辦／已知限制
- 尚未人工驗證各起始頁面（檔案庫、快剪、設定、關於）切換與全域快捷鍵在延後初始化下的行為。
- 尚未量測 Release 發行版（本次未啟用 ReadyToRun）。
- 若頁面 XAML 仍為主要成本，後續可將錄影頁右欄書籤清單改為 `x:Load` 延後載入。

## 2026-11-13 - v1.3.0

### 目標
補齊對外公開所需的專案文件與 MIT 授權，新增 ARM64 非 Self-Contained 發行，並將 x64／ARM64 壓縮檔發佈至 GitHub Release。

### 變更
- 新增 `LICENSE`（MIT，Copyright (c) 2026 mark216tw）。
- 重寫 `README.md`：專案說明、核心特色、系統需求、下載安裝、快速開始、快捷鍵、資料位置、專案結構、建置指令與文件索引。
- 新增 `docs/USER_GUIDE.md`：安裝、介面導覽、錄影、懸浮列、檔案庫、快剪、匯出、設定與疑難排解。
- 新增 `docs/ARCHITECTURE.md`：技術基線、分層架構、啟動生命週期、影音管線、服務模組、執行緒模型、儲存、Interop、錯誤處理與效能。
- 新增 `docs/DESIGN.md`：設計目標、使用情境、流程、錄影狀態機、頁面設計、資料模型、關鍵取捨、邊界情境與後續演進。
- 新增 `docs/BUILD.md`：開發環境、建置測試、x64／ARM64 發行、zip 封裝、版本管理與 Release 檢查清單。
- `Directory.Build.props`：版本更新至 1.3.0。
- `src/CourseRec/CourseRec.csproj`：`RuntimeIdentifiers` 加入 `win-arm64`。
- `CHANGELOG.md`、`RELEASE_NOTES.md`：新增 v1.3.0 條目與下載說明。

### 問題與解法
- ARM64 首次發行出現 `NETSDK1032`：`Directory.Build.props` 固定 `PlatformTarget=x64`，需同時指定 `-p:Platform=ARM64 -p:PlatformTarget=ARM64`。
- 非 Self-Contained 初版僅關閉 .NET 自包含，輸出缺少 Windows App SDK 原生組件且停用了 bootstrap；改以 `--self-contained false -p:WindowsAppSDKSelfContained=true`，讓目標機器只需 .NET 10 Runtime。
- 發行目錄含 `.pdb` 會增加 zip 體積；封裝時排除並補入 `LICENSE` 與 `README.md`。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：60/60 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0。
- x64 非 Self-Contained 發行可啟動（程序持續執行後手動停止）；PE 標頭為 x64。
- ARM64 非 Self-Contained 發行建置成功；PE 標頭為 ARM64，內含 Windows App Runtime 原生組件。
- 壓縮檔：`CourseRec-1.3.0-win-x64-framework-dependent.zip`（約 51.8 MB）、`CourseRec-1.3.0-win-arm64-framework-dependent.zip`（約 49.6 MB），均含執行檔、LICENSE、README，且無 `.pdb`。

### 待辦／已知限制
- ARM64 版本尚未於實機執行驗證，僅確認建置、封裝與 PE 架構。
- 尚未提供程式碼簽章，首次執行可能出現 SmartScreen 提示。
- 既有功能的人工驗證項目仍待補齊（長時間錄影、深色標題列、高 DPI 等）。

## 2026-11-13 - v1.2.0

### 目標
落實一輪「改動小、效益高」的介面與功能優化：提升長時間錄影可靠性、改善課後續看與檔案庫搜尋，並補強設定驗證與操作回饋。

### 變更
- `Interop/ExecutionStateNativeMethods.cs`、`RecorderViewModel`：錄影倒數開始時以 `SetThreadExecutionState` 阻止系統休眠與關閉螢幕，停止、取消、啟動失敗或釋放時恢復。
- `AppSettings`、`ISettingsService`、`SettingsService`：新增 `RecordingFileNamePrefix`、`LastEditorVideoPath`、`LastEditorPositionTicks` 與 `SaveAppearanceAsync`／`SaveRecordingPreferencesAsync`／`SaveLastEditorStateAsync`；局部更新改在 I/O 鎖內讀取、修改、寫入，避免快速切換時互相覆蓋。
- `Helpers/FileNameHelper.cs`：集中清理錄影檔名前綴，避免無效字元與空白造成失敗。
- `SettingsViewModel`、`SettingsPage`：外觀設定改為即時儲存；輸出資料夾儲存前驗證可建立、可寫入；新增檔名前綴；快捷鍵欄位支援按鍵捕捉與回復預設。
- `RecorderViewModel`、`RecorderPage`：記住情境／畫質／自動停止選擇；自動停止新增 5、10、15、20 分鐘；檔名套用前綴；啟動前包住輸出資料夾建立例外；書籤備註 Enter 新增與清單自動捲動；新增目前快捷鍵摘要。
- `EditorViewModel`、`EditorPage`：顯示音量增益與匯出進度百分比；載入上次影片與播放位置，播放中每 5 秒節流保存，離開頁面時再保存一次。
- `LibraryViewModel`、`LibraryPage`、`RecordingLibraryItem`：搜尋納入書籤備註、顯示合計時長、雙擊錄影資訊開啟快剪。
- `MainWindow`：設定與關於移至導覽列底部；標題顯示錄影狀態與時間。
- `ThemeService`、`WindowNativeMethods`：深色模式以 DWM 沉浸式深色標題列同步；不支援的系統維持預設。
- `Strings/zh-Hant/Resources.resw`：補齊新功能與錯誤文案。
- 版本更新至 v1.2.0。

### 問題與解法
- `SetThreadExecutionState` 以執行緒為單位，僅在 UI 執行緒建立與清除錄影期間的電源需求；若程序結束由系統回收，不影響正常停止流程。
- 快捷鍵輸入原為手動文字，容易格式錯誤；改在 `PreviewKeyDown` 捕捉支援的按鍵與修飾鍵組合，仍保留手動輸入作為後備。
- 上次播放位置若持續寫入會造成過多磁碟 I/O，改以 5 秒節流並在離開頁面時補寫；影片不存在時啟動自動忽略。
- 輸出資料夾驗證與錄影啟動建立資料夾都改為捕獲例外，避免非法路徑直接冒泡到全域例外處理。
- 主題外觀在 `SyncFromSettings` 期間以旗標抑制自動儲存，避免啟動或按儲存時重複寫檔。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：60/60 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異。

### 待辦／已知限制
- 尚未人工驗證長時間錄影休眠抑制、深色標題列在 Windows 10 1809 的行為，以及快捷鍵捕捉與中文輸入法互動。
- 尚未實測上次播放位置在強制關閉程式或影片被外部移動後的行為。
- 雙擊檔案庫資訊列開啟快剪尚未於觸控裝置與高 DPI 環境人工確認。

## 2026-11-13 - v1.1.2

### 目標
落實 P0 可靠性優化：確保關閉時日誌與資源完成收尾、修正剪貼簿跨執行緒、避免停止音訊凍結 UI、補強全域例外處理，並將使用者可見錯誤改為本地化友善文案。

### 變更
- `App.xaml.cs`：`Closed` 改為同步等待 `StopAsync` 與 `Dispose`（背景執行停止、UI 執行緒釋放），加入 `AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`；UI 未處理例外改為顯示可複製診斷資訊的 `ContentDialog` 後結束程式。
- `Helpers/ClipboardHelper.cs`：新增 UI 執行緒剪貼簿輔助，統一處理 `Clipboard.SetContent` 與 `Flush`。
- `Services/Capture/ScreenCaptureService.cs`、`Services/Editing/MediaEditingService.cs`：快拍與匯出複製改用 `ClipboardHelper`，避免在背景執行緒呼叫 WinRT 剪貼簿。
- `Services/Audio/IAudioCaptureService.cs`、`AudioCaptureService.cs`：新增 `StopMonitoringAsync`；同步版本保留給 `Dispose`，等待上限 2 秒且逾時後於背景完成清理。
- `ViewModels/RecorderViewModel.cs`：離開錄影頁改為非阻塞停止音訊，停止錄影流程改為 `await StopMonitoringAsync()`。
- `RecorderViewModel`、`EditorViewModel`、`LibraryViewModel`、`SettingsViewModel`、`AboutViewModel`：使用者可見錯誤改為本地化文案，原始例外只寫入日誌。
- `Strings/zh-Hant/Resources.resw`：新增錯誤、快拍、匯出、檔案庫、設定、關於與重大錯誤對話框字串；移除含 HRESULT 的舊錄影錯誤格式。
- 版本更新至 v1.1.2。

### 問題與解法
- `Closed` 事件中的 `async void` 可能在訊息迴圈結束後才恢復，導致 `StopAsync`／日誌排空沒有完成；改為在關閉事件同步等待，停止主機放到背景執行以避免死鎖，資源釋放留在 UI 執行緒。
- WinRT 剪貼簿需在 UI/ASTA 執行緒呼叫；改以 `DispatcherQueue.TryEnqueue` 排回 UI，並在無法排入時回報例外給呼叫端記錄。
- 音訊停止原本在 UI 執行緒 `Wait(2s)`；改為 `WaitAsync` 等待，逾時只記警告並讓擷取迴圈自行結束，避免導覽或停止錄影時卡頓。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：54/54 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過。

### 待辦／已知限制
- 尚未人工驗證重大錯誤對話框、剪貼簿複製與關閉時日誌排空；需在實機觸發例外與關閉流程確認。
- 尚未人工量測停止音訊在裝置失效或驅動卡住時的 2 秒逾時行為。
- 舊的 `Recording*Format` 資源已由新文案取代；若有外部自訂資源檔需同步更新。

## 2026-11-13 - v1.1.1

### 目標
縮小錄影工作台頁面「標題列」與下方工作區之間的垂直間隔，讓版面更緊湊。

### 變更
- `Views/RecorderPage.xaml`：外層 `StackPanel` 的 `Spacing` 由 16 改為 8；左右欄卡片內部間距維持 16 不變。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 成功，0 警告、0 錯誤。

### 待辦／已知限制
- 尚未實機檢視不同 DPI 下的視覺密度。

## 2026-11-13 - v1.1.0

### 目標
實作一輪「改動小、效益高」的介面與功能優化：補齊錯誤回饋與防呆、常用資料夾入口、檔案庫排序與虛擬化、快剪匯出取消與鍵盤操作、錄影輔助資訊，以及記住上次頁面。

### 變更
- 設定頁：錯誤提示改綁 `StatusSeverity`，成功訊息自動收起；新增輸出資料夾開啟按鈕；儲存前驗證快捷鍵格式，並在註冊被佔用時顯示警告。
- 快剪：載入其他影片前以 `ContentDialog` 確認未匯出的剪輯變更；匯出加入取消按鈕與 `CancellationToken`；新增空白鍵播放／暫停、方向鍵跳轉與上／下個書籤快捷鍵及對應按鈕。
- 錄影工作台：倒數期間停止按鈕改為「取消倒數」並顯示取消提示；新增快拍資料夾按鈕、工作階段書籤刪除、自動停止剩餘時間。
- 迷你懸浮列：停止按鈕文字／提示跟隨倒數狀態，新增系統與麥克風兩條即時音量條。
- 檔案庫：新增排序（最新／最舊／檔名／大小／時長）並持久化；搜尋加入清除按鈕、`Ctrl+F` 聚焦與 `Esc` 清空；版面改為固定表頭＋可捲動清單，恢復 `ListView` 虛擬化。
- 關於頁：新增開啟輸出資料夾、開啟日誌資料夾與複製版本資訊。
- 導覽：記住上次瀏覽頁面；啟動時即使不是錄影頁，也會初始化錄影 ViewModel 並暫停閒置擷取，確保全域快捷鍵可用。
- `AppSettings`、`SettingsService`：新增 `LibrarySortOrder`、`LastPage` 與對應儲存方法；設定頁儲存時保留這兩個欄位。
- 新增檔案庫排序、設定持久化、未儲存變更確認、錄影書籤刪除與自動停止剩餘時間測試；版本更新至 v1.1.0。

### 問題與解法
- `ListView` 原本包在外層 `ScrollViewer` 會關閉虛擬化；改為 `Grid` 列配置，讓清單自行捲動。
- 記住上次頁面後，若啟動頁不是錄影頁，`RecorderViewModel` 原本要等錄影頁載入才初始化，會導致全域快捷鍵沒有目標；改在 `App.OnLaunched` 先 `AttachDispatcher`，非錄影頁時再 `SuspendIdleCapture`。
- 設定頁儲存會建立新的 `AppSettings`，若未複製新欄位會把排序與上次頁面重設；已補上 `LibrarySortOrder`、`LastPage`。
- 匯出取消沿用既有 `MediaEditingService` 的暫存檔提交／清理機制，取消時不取代既有輸出。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：54/54 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過，無格式差異。

### 待辦／已知限制
- 尚未人工驗證檔案庫虛擬化在大量錄影檔下的捲動、縮放與高 DPI 版面。
- 尚未人工驗證迷你懸浮列新增音量條後的寬度、DPI 與拖曳行為。
- 尚未實測快捷鍵被其他程式佔用時的警告流程，以及匯出取消後的暫存檔清理。
- 匯出中關閉主視窗目前仍沿用既有關閉流程，未額外加入匯出專用確認。

## 2026-11-13 - v1.0.18

### 目標
降低背景資源消耗、減少不必要的 UI／磁碟／檔案 I/O，並提升主題切換與檔案庫重複進入的流暢度。

### 變更
- `ViewModels/RecorderViewModel.cs`、`Views/RecorderPage.xaml.cs`：新增 `SuspendIdleCapture`／`ResumeIdleCapture`；離開錄影頁時關閉閒置預覽與音訊監控，錄影中僅停用預覽縮圖；從全域快捷鍵於背景開始錄影時確保 WASAPI 監控已啟動。
- `Services/Settings/ThemeService.cs`：只有顏色主題變更時才重建資源字典。
- `Services/Editing/MediaEditingService.cs`：多段匯出建立一次 `MediaClip`，其餘區間使用 `Clone()`。
- `Helpers/LocalizationService.cs`：加入字串解析快取。
- `ViewModels/LibraryViewModel.cs`、`Helpers/RangeObservableCollection.cs`：以檔案與書籤時戳／大小快取掃描結果，來源與搜尋清單改用單次 `ReplaceAll`。
- `ViewModels/RecorderViewModel.cs`：磁碟空間摘要改為 5 秒節流並在背景計算；書籤自動保存改為僅變更時或有書籤時每分鐘寫入。
- 版本更新至 v1.0.18。

### 問題與解法
- 暫停音訊監控後，若使用者從其他頁面按全域快捷鍵開錄，會沒有音訊緩衝；在 `StartRecordingAsync` 補上 `StartMonitoring` 確保錄影前監控已啟動。
- 暫停後從快捷鍵快拍會重新啟動擷取；`SyncScreenCaptureState` 在閒置且頁面隱藏時改為停止擷取，避免快拍後又持續預覽。
- 主題資源字典原本每次 `ApplyTheme` 都重建，造成設定頁切換時重複解析；改以 `paletteChanged` 判斷，只更新 `RequestedTheme`。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：49/49 通過（新增檔案庫掃描快取測試）。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過。

### 待辦／已知限制
- 未實測背景錄影時切換頁面、全域快捷鍵快拍與多螢幕／不同 DPI 的組合情境。
- 螢幕擷取在閒置頁面關閉後，回到頁面仍需重新建立擷取工作階段，可能有一小段預覽延遲。

## 2026-11-13 - v1.0.17

### 目標
移除「護眼綠」主題顏色、新增 3 秒錄影倒數選項，並在錄影檔案庫顯示全部錄影檔合計大小。

### 變更
- `Models/AppColorTheme.cs`：`EyeCareGreen` 標註為已停用，僅保留舊設定檔讀取相容。
- `Services/Settings/ThemeService.cs`：移除護眼綠主題字典對應；刪除 `Styles/Themes/EyeCareGreenTheme.xaml`。
- `ViewModels/SettingsViewModel.cs`：主題下拉移除護眼綠；倒數選項新增 3 秒；同步設定時的備援值改為明確 5 秒。
- `Services/Settings/SettingsService.cs`：`EyeCareGreen`（含舊版 `themePreference`）載入時自動轉為 `EyeCareYellow`；倒數秒數改以 `{0,3,5,10,15,20,25,30}` 白名單取最接近值。
- `ViewModels/LibraryViewModel.cs`、`Views/LibraryPage.xaml`：新增 `TotalSizeText`／`TotalSizeVisibility`，顯示全部錄影檔合計大小並隨重新掃描與刪除更新。
- `Strings/zh-Hant/Resources.resw`：移除 `ThemeEyeCareGreenText`、更新主題 Automation 名稱與倒數說明，新增 `LibraryTotalSizeFormat`。
- `tests/CourseRec.Tests`：新增 3 秒倒數、白名單對應、護眼綠遷移與檔案庫總大小測試。
- 版本更新至 v1.0.17。

### 問題與解法
- `AppColorTheme` 以 `JsonStringEnumConverter` 序列化，直接刪除 `EyeCareGreen` 會讓舊設定檔反序列化失敗並重置全部設定；保留列舉值並在正規化時轉為 `EyeCareYellow`，UI 與主題字典皆不再提供。
- 倒數正規化原本以 5 秒四捨五入，3 會被改成 5；改為白名單取最接近值，維持既有 0/5/10/15/20/25/30 行為。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：48/48 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：通過，無格式差異。

## 2026-11-13 - v1.0.16

### 目標
修正「跟隨系統」模式下切換主題顏色無效的問題，讓迷你控制列記住上次位置，並新增可直接關閉懸浮列的按鈕。

### 變更
- `Services/Settings/ThemeService.cs`：改以 `ActualTheme` 判斷真正的相反明暗再切回目標主題，確保 `ElementTheme.Default` 在系統淺色時也會刷新 `ThemeResource`；新增 `AppliedColorTheme` 只在色系變更時重建字典。
- `Models/MiniOverlayPosition.cs`、`Models/AppSettings.cs`：新增迷你控制列位置模型與設定欄位。
- `Services/Settings/ISettingsService.cs`、`SettingsService.cs`：新增 `SaveMiniOverlayPositionAsync`，並保留位置欄位。
- `ViewModels/SettingsViewModel.cs`：儲存設定時保留 `MiniOverlayPosition`，避免被清空。
- `Views/MiniOverlayWindow.xaml.cs`：開啟後還原上次位置並以 `DisplayArea.WorkArea` 夾回可見區域；拖曳結束與關閉時儲存位置。
- `Views/MiniOverlayWindow.xaml`、`.xaml.cs`：新增 `MiniBtnClose` 關閉按鈕，點擊只收起懸浮列，不停止錄影。
- `Views/RecorderPage.xaml.cs`：建立迷你控制列時傳入 `ISettingsService`。
- `Strings/zh-Hant/Resources.resw`：新增關閉按鈕的 ToolTip 與 Automation 名稱。
- 版本檔案更新至 v1.0.16。

### 問題與解法
- 「跟隨系統」＋系統淺色時，原先的刷新邏輯把 `Default` 切成 `Light`，實際主題沒有改變，`ThemeResource` 不會重新解析；改為依 `ActualTheme` 切到真正相反的主題再切回。
- 迷你控制列位置以實體像素儲存，還原時若落在已移除的螢幕或工作區外，會被夾回最近的可見工作區。
- 關閉按鈕直接呼叫 `Close()`，沿用既有 `Closed` 流程取消主題註冊並重新整理 `RecorderPage` 內的視窗參考。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：44/44 通過（含迷你控制列位置儲存測試）。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異（僅有工作區載入警告）。

### 待辦／已知限制
- 尚未人工驗證多螢幕拔除、DPI 變更後的位置還原。
- 尚未人工確認關閉懸浮列後錄影持續進行與重新開啟位置一致。

---

## 2026-11-13 - v1.0.15

### 目標
重新整理顯示主題設定：拆成「顯示模式」與「主題顏色」兩個獨立選項，讓主題顏色的深淺跟隨顯示模式；同時新增獨立「關於」頁面，並縮小迷你懸浮控制列、確認其樣式跟隨主視窗。

### 變更
- `Models/AppDisplayMode.cs`、`Models/AppColorTheme.cs`：新增顯示模式（跟隨系統／淺色／深色）與六種主題顏色（護眼黃、護眼綠、海洋藍、森林綠、暖陽橘、薰衣草紫）。
- `Models/AppSettings.cs`、`Models/CourseRecDefaultOptions.cs`、`appsettings.json`：改存 `DisplayMode` 與 `ColorTheme`，保留舊 `themePreference` 遷移欄位。
- `Services/Settings/SettingsService.cs`：舊版合併式主題值自動轉換為新的顯示模式與顏色；新安裝預設跟隨系統＋海洋藍。
- `Services/Settings/IThemeService.cs`、`ThemeService.cs`：改以顯示模式與主題顏色套用主題，並同步所有已註冊視窗（含迷你控制列）。
- `Styles/Themes/EyeCareYellowTheme.xaml`、`EyeCareGreenTheme.xaml`：補上深色配色，`Default` 與 `Dark` 一致。
- `Views/SettingsPage.xaml`、`ViewModels/SettingsViewModel.cs`：外觀卡片改為兩個下拉選單；儲存按鈕移出卡片；移除關於區塊。
- 新增 `Views/AboutPage.xaml`、`Views/AboutPage.xaml.cs`、`ViewModels/AboutViewModel.cs`；`MainWindow` 導覽新增「關於」項目，`NavigationPageKey` 新增 `About`。
- `Views/MiniOverlayWindow.xaml`：縮小內距、按鈕高度、指示燈與進度環尺寸，降低整體列高；外層加入 `Grid` 主題根，讓 `RequestedTheme` 與自訂色系字典確實傳遞到圓角列。
- `Strings/zh-Hant/Resources.resw`：新增顯示模式、主題顏色與關於頁面字串，移除舊合併式主題字串。
- 版本檔案更新至 v1.0.15。

### 問題與解法
- 舊設定檔只有 `themePreference`，讀取後以 `LegacyThemePreference` 映射為新組合，寫入時不再輸出舊欄位，避免使用者升級後主題遺失。
- 護眼黃／護眼綠原本的深色字典直接複製淺色值，改為真正的深色色階，讓顯示模式切換有明顯差異。
- 迷你控制列高度受按鈕預設高度與 `Border` 上下內距影響，同步調降按鈕 `Padding`／`MinHeight` 與間距後縮小列高。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：43/43 通過（含舊主題設定遷移測試）。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異（僅有工作區載入警告）。

### 待辦／已知限制
- 尚未人工驗證六種主題顏色在淺色／深色／跟隨系統下的實際對比度與迷你控制列外觀。
- 尚未實測舊版 `settings.json` 在各種 `themePreference` 值下的升級流程。

---

## 2026-11-13 - v1.0.14

### 目標
修正迷你懸浮控制列三個操作問題：拖曳後游標被吸住需再點一下才放開、按鈕超出視窗右緣、以及錄影時間改放到狀態文字下方。

### 變更
- `MiniOverlayWindow.xaml`：根 `Border` 具名並加入 `Loaded`／`SizeChanged` 與 `PointerMoved`／`PointerReleased`／`PointerCaptureLost`；左側改為指示燈＋直排文字（`StatusBadgeText` 在上、`ElapsedTimeText` 在下），右側按鈕群獨立為 `Auto` 欄，按鈕間距縮為 4。
- `MiniOverlayWindow.xaml.cs`：移除 `WM_NCLBUTTONDOWN`／`HTCAPTION` 的 modal 拖曳，改以 `GetCursorPos` 與 `AppWindow.Move` 實作受控拖曳；新增依 `XamlRoot.RasterizationScale` 量測內容並以 `AppWindow.ResizeClient` 自動調整視窗大小；訂閱 ViewModel 屬性變更以在狀態／時間文字變長時重新量測；關閉時解除訂閱。
- `WindowNativeMethods`：新增 `GetCursorPos` 與 `Point`；移除不再使用的 `WM_NCLBUTTONDOWN`、`HTCAPTION`、`ReleaseCapture`、`SendMessage`。
- 版本檔案更新至 v1.0.14。

### 問題與解法
- 游標吸住：在 XAML `PointerPressed` 內 `ReleaseCapture` 後送 `WM_NCLBUTTONDOWN` 會進入系統 modal 拖曳迴圈，但指標已被 XAML 擷取，放開訊息被 WinUI 吃掉，迴圈收不到 `WM_LBUTTONUP`，需再點一次才退出；改為自行擷取指標並在 `PointerMoved` 依游標位移移動視窗，不再進入 modal 迴圈。
- 右緣溢出：`AppWindow.Resize(460, 68)` 的單位是實體像素，125%／150% DPI 下可用 DIP 不足導致按鈕被裁切；改以內容自然尺寸乘上 `RasterizationScale` 後用 `ResizeClient` 設定客戶區大小，並加上 2px 容差避免量測與 DPI 對齊造成重複 resize。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：42/42 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異。

### 待辦／已知限制
- 尚未人工驗證 100%／125%／150%／200% DPI 下拖曳與寬度自適應，以及多螢幕邊界被推出畫面時的位置限制。
- `AppWindow.Move` 為逐次指標事件移動，尚未實測高更新率滑鼠的順暢度。

---

## 2026-11-13 - v1.0.13

### 目標
完成 P0 + P1 低改動高效益優化：降低 UI 執行緒負擔、提升長時間錄影時間精度與穩定性，並減少音訊熱路徑配置。

### 變更
- `RecordingFrameTiming`、`RecorderViewModel`：影格 PTS 改以累計有理數計算，消除固定時長截斷造成的 A/V 累積漂移；編碼 pump 改用 `LongRunning` 專用執行緒與 `ManualResetEventSlim` 暫停閘門。
- `RecordingTimelineClock`：Start／Pause／Resume／Reset／查詢全部加上鎖，避免 UI 與 pump 執行緒競態。
- `RecorderViewModel`：`Mp4EncoderService.Open` 移至背景執行緒，取消或例外時補上 `FinalizeRecording` 清理。
- `RecorderViewModel`、`CaptureEnumerationService`、`WindowIconHelper`：來源列舉改非同步，程序名稱與執行檔圖示加入快取，避免 UI 凍結與重複 Shell 查詢。
- `BgraScaler`、`ScreenCaptureService`、`RecorderPage`：新增 2 槽預覽縮圖環形緩衝（長邊上限 1280），只將縮圖送到 UI；`WriteableBitmap` 資料流改為快取；全解析度影格仍供編碼與 PNG 快拍。
- `DualTrackAudioMixer`、`AudioCaptureService`：新增 `TryConvertToStereo48kHz` 與可重用轉換緩衝；stackalloc 門檻提高到 8192；待機輪詢由固定 15ms 改為錄影中 15ms、待機 30ms。
- `D3D11NativeMethods`、`ScreenCaptureService`：回報 Map／建立 staging texture 的 HRESULT，偵測 `DXGI_ERROR_DEVICE_REMOVED`／`RESET` 與連續失敗；以獨立工作在退避後重建 D3D 裝置與擷取工作階段。
- 新增精確 PTS、預覽尺寸、device lost HRESULT、音訊轉換 overload、慢速 encoder 取消清理等測試。

### 問題與解法
- 影格 PTS 原本先截斷每幀時長再乘上影格索引，1 小時約累積 3.6ms 誤差；改與音訊相同的累計商算法後誤差固定在 1 tick 內。
- 暫停時原本每 25ms 喚醒 pump；改用閘門後暫停期間不再輪詢，恢復時立即放行。
- 預覽若直接縮圖會與編碼共用 4 槽緩衝，因此另建 2 槽預覽環並沿用 `CapturedFrameData` 參照計數，避免覆寫使用中的影格。
- 裝置復原以 `captureStopVersion` 區分使用者主動停止／切換來源與內部重建，避免延遲復原把已停止的擷取重新啟動。

### 驗證
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：42/42 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異。

### 待辦／已知限制
- 尚未人工驗證 4K／1440p 高 DPI 預覽順暢度、數十個視窗（含無回應視窗）列舉，以及 2 小時以上 A/V 同步。
- D3D device lost 復原僅以 HRESULT 判斷與單元測試涵蓋判斷邏輯，尚未實機模擬 GPU 重置／驅動更新。

---

## 2026-11-13 - v1.0.12

### 目標
在錄影工作台的來源下拉清單每一列前顯示圖示：視窗顯示應用程式圖示，顯示器顯示系統「桌上型電腦」圖示。

### 變更
- `CaptureTarget`、`IconData`：新增來源圖示資料（BGRA、預乘 alpha），不改變既有建構子參數，並讓 `ToString()` 回傳標題以支援下拉選單鍵盤搜尋。
- `CaptureIconNativeMethods`、`WindowIconHelper`：以 `WM_GETICON`（`SendMessageTimeout`，避免無回應視窗阻塞 UI）、`GetClassLongPtr`、`SHGetFileInfo` 依序取得視窗圖示；`SHGetStockIconInfo(SIID_DESKTOPPC)` 取得顯示器圖示；`GetIconInfo` + `GetDIBits` 轉為像素資料，無 alpha 時以 AND mask 補透明度，並快取顯示器圖示。
- `CaptureEnumerationService`：列舉時填入圖示，失敗僅寫入 Debug 記錄不影響來源列舉；建構子新增選用 `ILogger`。
- `IconDataToImageSourceConverter`、`App.xaml`、`RecorderPage.xaml`：註冊轉換器，`CmbCaptureTarget` 改為 `ItemTemplate`（16×16 圖示 + 標題，取不到圖示時以內建監視器符號備援）。
- 新增 `IconExtractionTests`：驗證顯示器圖示可解碼為正確長度的 BGRA 緩衝區。
- 版本與文件更新至 v1.0.12。

### 問題與解法
- `LibraryImport` 搭配含 `fixed char` 的 `SHSTOCKICONINFO` 呼叫 `SHGetStockIconInfo` 會回傳 `E_INVALIDARG`；改用 `DllImport` 後正常（`SHGetFileInfo` 一併改為 `DllImport`）。
- `SHGetStockIconInfo` 在多執行緒同時呼叫時偶發 `E_OUTOFMEMORY` 並取不到圖示；改為加鎖並重試一次，同時快取顯示器圖示（實際只在 UI 執行緒呼叫）。
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：36/36 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異。

### 待辦／已知限制
- 受保護或提高權限的程序可能取不到 exe 圖示，會退回內建監視器符號。
- 尚未人工確認高 DPI 與高對比主題下的圖示清晰度與對齊。

---

## 2026-11-13 - v1.0.11

### 目標
補充視窗擷取最小化行為說明，並精簡錄影工作台、檔案庫與設定頁的文字與按鈕呈現。

### 變更
- `RecorderViewModel`、`RecorderPage`、`Resources.resw`：選擇「指定應用程式視窗」時，於擷取類型下方顯示最小化後仍持續錄音、畫面停留在最後一幀的說明。
- `RecorderPage`、`Resources.resw`：「重新整理來源」改為 `Refresh` 圖示按鈕，保留自動化名稱與 ToolTip，移除按鈕文字資源。
- `Resources.resw`：檔案庫選單與相關按鈕文字由「顯示檔案位置」改為「開啟檔案目錄」，行為仍為開啟檔案總管並選取檔案。
- `Resources.resw`：設定頁懸浮列不入鏡開關的啟用文字移除 `WDA_EXCLUDEFROMCAPTURE`，改為「已啟用」。
- 版本與文件更新至 v1.0.11。

### 問題與解法
- `x:Bind` 不會自動將 `bool` 轉為 `Visibility`，沿用 ViewModel 既有模式新增 `WindowTargetHintVisibility`，切換擷取類型時同步通知。
- 圖示按鈕若保留 `.Content` 資源會覆蓋 `SymbolIcon`，因此移除該資源鍵，僅保留無障礙名稱與 ToolTip。
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：首次執行遇到書籤並行測試偶發 `UnauthorizedAccessException`，重跑後 35/35 通過；與本次 UI 變更無關。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異。

### 待辦／已知限制
- 尚未人工確認不同視窗寬度下說明文字的換行，以及高對比主題中的圖示按鈕顯示。

---

## 2026-11-13 - v1.0.10

### 目標
改善迷你懸浮列操作體驗、錄影狀態辨識與檔案庫快速播放。

### 變更
- `WindowNativeMethods`、`MiniOverlayWindow`：懸浮列可用滑鼠拖曳移動；停止錄影按鈕移至暫停按鈕右側；最右側按鈕改為「開啟主視窗」圖示與提示。
- `MiniOverlayWindow`、`INavigationCoordinator`：由懸浮列停止錄影後立即切回錄影工作台並帶出主視窗，MP4 封裝仍在背景完成。
- `ITaskbarIndicatorService`、`TaskbarIndicatorService`、`ITaskbarList3`、`TaskbarNativeMethods`、`Assets/RecordingIndicator.ico`、`RecorderViewModel`、`App`：錄影中於主視窗工作列圖示疊加紅色圓點，停止或待命時移除。
- `LibraryViewModel`、`LibraryPage`、`Resources.resw`：檔案庫於「快剪與複習」左側新增「播放」，以系統預設播放器開啟 MP4。
- 版本與文件更新至 v1.0.10。

### 問題與解法
- 懸浮列沒有標題列，改用 `WM_NCLBUTTONDOWN`／`HTCAPTION` 觸發 Windows 原生視窗移動；拖曳前檢查是否按在按鈕內，避免搶走按鈕點擊。
- 工作列指示採用 `ITaskbarList3.SetOverlayIcon` 疊加紅點，不替換應用程式主圖示；COM 介面需依 vtable 順序完整宣告至 `SetOverlayIcon`。
- `dotnet build CourseRec.sln -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：35/35 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異。

### 待辦／已知限制
- 尚未人工驗證多螢幕／不同 DPI 下的懸浮列拖曳、Windows 10 1809 工作列覆蓋圖示，以及系統缺少預設播放器關聯時的提示。

---

## 2026-11-06 - v1.0.9

### 目標
落實五項局部架構優化：編碼錯誤回報、影格使用權、匯出暫存提交、錄影書籤自動保存及背景批次日誌。

### 變更
- `Mp4EncoderService`、`RecorderViewModel`：編碼／封裝失敗傳遞至錄影狀態，背景編碼 Task 立即觀察並停止錄影；失敗時保留部分檔案與書籤，不顯示成功。
- `CapturedFrameData`、`ScreenCaptureService`、`RecorderPage`：參照計數使用權保護四槽緩衝區，最新影格快照由呼叫者釋放；預覽只保留最新待處理影格，入列失敗與頁面卸載時釋放使用權。
- `MediaEditingService`：同資料夾唯一暫存檔渲染成功後再提交，取消／失敗清理暫存檔並保留目的檔，禁止來源與目的相同。
- `RecorderViewModel`、`BookmarkService`：書籤於約 1.5 秒維護週期背景保存、每 30 秒更新時長，失敗重試，停止時等待保存並寫入最終快照；書籤檔寫入序列化。
- `FileLogger`、`FileLoggerProvider`、`App`：共用有界 Channel 與背景批次寫入、重複訊息彙總、滿載遺失計數、5 MB 輪替及正常關閉排空；Provider 改由 DI 管理生命週期。
- 新增錄影失敗、書籤自動保存／重試與並行寫入、影格並行使用權及背景日誌回歸測試，擴充原生匯出取消與提交失敗的檔案保護測試；版本與文件更新至 v1.0.9。

### 問題與解法
- 確認編碼／封裝 HRESULT 失敗僅記錄警告、環狀陣列可能在讀取時覆寫、匯出預先清空目的檔、錄影書籤僅停止時落盤、日誌同步寫入等問題，逐項修正。
- `dotnet build CourseRec.sln --no-restore -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- 新增 8 項錄影／書籤／影格／日誌測試通過；原生整合測試在進度回呼內同步取消會重入 WinRT 內部鎖而等待不結束，改由 `CancelAsync` 在回呼外執行取消通知，再驗證舊檔與暫存檔清理。
- 提交遭目的檔鎖定時，Windows `File.Move` 可能回報 `UnauthorizedAccessException` 而非 `IOException`；測試涵蓋兩種平台例外，重點驗證舊檔內容不變及暫存檔清除。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：35/35 通過，含真實 Media Foundation 編碼、MP4／M4A 匯出、取消與提交失敗保護。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異；工具回報工作區載入警告。

### 待辦／已知限制
- 尚未進行數小時 4K 錄影、多 DPI 預覽與舊版 Windows 10 人工壓測。
- 自動保存降低異常結束時的書籤遺失範圍，但不保證斷電時尚未落盤的資料；日誌滿載或磁碟持續不可用時採不阻塞錄影策略，可能遺失日誌並於恢復時彙總計數。

---

## 2026-11-06 - v1.0.8

### 目標
全面落實底層影音擷取、編碼、混音、快剪匯出與跨頁檔案鎖定架構優化，消除高頻記憶體配置與裝置相容性隱患。

### 變更
- `IScreenCaptureService`、`ScreenCaptureService`、`D3D11NativeMethods`、`RecorderViewModel`：待機且關閉即時預覽時停止背景擷取（按需快拍時暫時啟動）；新增依幀率節流的 GPU Readback 與 4 槽環狀像素緩衝區重用，移除每幀 LOH 大陣列配置與重複記憶體拷貝。
- `MediaFoundationNativeMethods`、`IMp4EncoderService`、`Mp4EncoderService`：啟用 H.264 High Profile（失敗自動降級 Main）、2 秒關鍵影格間距（GOP）、`MFTranscodeContainerType_FMPEG4` 分段式 MP4 防斷電封裝（失敗自動降級標準 MP4）、基於 COM 參照計數獨佔檢查的 `IMFMediaBuffer` 緩衝池，以及優先委派內建 Video Processor MFT 縮放；`ScaleBgraIntoBuffer` 升級為定點雙線性插值與局部黑邊清除。
- `WasapiNativeMethods`、`AudioCaptureService`、`DualTrackAudioMixer`：端點擷取優先啟用 `AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY` 核心重取樣；將 Process Loopback 非同步啟動參數與原生事件控制代碼併入 COM 狀態結構並隨參照計數歸零釋放；補齊 5.1／7.1 環繞中置人聲混音，混音緩衝改用 `stackalloc`／`ArrayPool<float>`。
- `MediaEditingService`：未變更音量的 MP4 匯出改用 `MediaTrimmingPreference.Fast` 快速剪輯；AI 逐字稿 M4A 改為 48kHz 單聲道 96kbps AAC。
- `INavigationCoordinator`、`NavigationCoordinator`、`EditorViewModel`、`EditorPage`、`LibraryViewModel`、`FileLogger`：離開快剪頁即釋放 `MediaSource` 檔案鎖定並防止重複訂閱時長事件，檔案庫改名／刪除時自動釋放並同步編輯器狀態；無書籤錄影亦寫入時長中繼檔並支援檔案庫並行掃描；日誌加入 5 MB 自動輪替。
- 擴充多聲道混音、雙線性縮放、硬體端到端編碼與快剪 MP4／單聲道 M4A 匯出、跨頁改名／刪除同步等單元與整合測試；版本與文件更新至 `v1.0.8`。

### 問題與解法
- 非同步硬體 H.264 MFT 於 `WriteSample` 返回後仍可能短暫持有 `IMFMediaBuffer` → 透過 `IUnknown::AddRef/Release` 檢查參照計數為 1 才重用池內緩衝區，兼顧零配置與執行緒安全。
- Process Loopback `ActivateAudioInterfaceAsync` 若於 3 秒逾時後才完成回呼，原本堆疊參數與 `ManualResetEvent` 已釋放可能造成無效控制代碼存取 → 改以原生堆積狀態結構持有 `CreateEventW` 控制代碼與參數，待最終 COM `Release` 歸零時才 `CloseHandle` 與釋放記憶體。
- 編輯器與檔案庫同步測試中刪除前先請求釋放再通知刪除會觸發兩次釋放事件 → 於 `EditorViewModel` 記錄媒體附加狀態，確保單次載入僅觸發一次釋放通知。
- `dotnet build CourseRec.sln --no-restore -m:1`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：27/27 通過（含真實 Media Foundation 編碼與 `MediaComposition` 快速匯出整合驗證）。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異。

### 待辦／已知限制
- 若欲進一步徹底免除高更新率 4K 錄影時的 GPU→CPU Readback，後續可評估接入 `IMFDXGIDeviceManager` 與 `MFCreateDXGISurfaceBuffer` 全 GPU 管線。
- 尚未於實體舊版 Windows 10 1809 與外接 7.1 USB 耳機進行人工實機壓測。

---

## 2026-11-06 - v1.0.7

### 目標
常駐顯示實際錄音來源、提供書籤修改／刪除，並將檔案庫次要操作收入更多選單。

### 變更
- `AudioCaptureStatus`、`SystemAudioSource`、音訊服務：回報實際來源、有效 PID、降級及麥克風可用狀態，停用／擷取失效時同步更新，定期重試失效的來源。
- `RecorderViewModel`、工作台：常駐顯示實際聲音來源與麥克風狀態，與短暫通知分開。
- 編輯器：書籤右鍵／更多選單、備註修改對話框、刪除確認；新增、修改與刪除均先寫入再更新清單，避免失敗時誤刪畫面資料。
- 編輯器在載入、儲存、匯出期間避免交錯操作；書籤變更保持時間戳與既有剪輯狀態。
- 檔案庫：次要按鈕收進更多選單，沿用改名與刪除確認；長檔名省略與提示。
- 新增書籤持久化、最後一筆刪除、匯出期間操作保護與寫入失敗回歸測試；版本及文件更新至 v1.0.7。

### 問題與解法
- 聲音隔離開關代表使用者偏好，不能代表 Process Loopback 是否成功；改由原生擷取成功結果回報狀態。
- 書籤儲存失敗可能造成畫面與 JSON 不一致，改成寫入成功後才套用清單；新增也沿用相同保護。回歸測試以暫存路徑衝突模擬寫入失敗，驗證原資料保持不變。
- `dotnet build CourseRec.sln`：Debug x64 建置成功，0 警告、0 錯誤。
- `dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build`：21/21 通過。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore`：結束碼 0，無格式差異；工具仍回報工作區載入警告。

### 待辦／已知限制
- 尚未人工實測 Process Loopback 成功／降級、音訊裝置熱插拔，以及各 DPI 下右鍵／更多選單與對話框操作。
- 目標程式來源顯示實際 PID；來源已建立不代表當下有聲音，仍須搭配音量表確認。

---

## 2026-11-06 - v1.0.6

### 目標
實作第一批低成本高效益優化：錄影狀態同步、提示分級、完成後快捷操作、檔案庫搜尋及快剪重設。

### 變更
- `RecorderViewModel`、工作台與迷你控制列：共用錄影狀態顯示色點、文字、暫停／繼續圖示與封裝進度；迷你列加寬容納狀態文字。
- 錄影／編輯器：通知依成功、警告、錯誤分級；成功提示 6 秒後收起，保留結果快捷操作。
- `FileActionHelper`：共用結果開啟與檔案定位；錄影完成可直接導向快剪。
- `LibraryViewModel`、檔案庫：即時檔名搜尋、筆數及無結果提示，重新整理、改名、刪除後維持篩選。
- 編輯器新增確認後重設剪輯，恢復完整範圍、清除待剪區間及增益，保留書籤與匯出檔。
- 繁體中文資源、版本及使用者文件更新；擴充搜尋與重設的回歸測試。

### 問題與解法
- 使用既有錄影狀態與主題筆刷呈現狀態，避免另建一套狀態來源。
- 第一次 `dotnet test --no-build` 讀到 `bin/Debug` 舊測試組件（12 項，含舊解析度測試失敗）；改為指定 `-p:Platform=x64`，確認測試 `bin/x64/Debug` 本次建置組件，17 項全數通過。
- 驗證：`dotnet build CourseRec.sln` 成功，0 警告、0 錯誤；`dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj -p:Platform=x64 --no-build` 通過 17/17。
- `dotnet format CourseRec.sln --verify-no-changes --no-restore` 結束碼 0，無格式差異；工具回報工作區載入警告。

### 待辦／已知限制
- 尚未人工驗證各 DPI 下的迷你列與按鈕配置，以及實際錄影、開啟預設播放器與檔案總管操作。

---

## 2026-11-05 - v1.0.5

### 目標
保留預覽開關左側的開啟／關閉狀態文字，同時徹底移除開關右側可能殘留的內容文字。

### 變更
- `RecorderPage`：`ToggleSwitch` 明確設定 `OnContent=""`、`OffContent=""`，並移除 `x:Uid` 對應。
- `RecorderViewModel`：新增 `LivePreviewAutomationName`，改用 `AutomationProperties.Name` 綁定。
- `Resources.resw`：新增 `LivePreviewAutomationName`，移除舊的 `SwLivePreview` 自動化資源鍵。
- `README.md`、`CHANGELOG.md`、`RELEASE_NOTES.md`、版本檔：更新至 v1.0.5。

### 問題與解法
- 僅移除 On/Off 資源仍可能受 `x:Uid` 或範本影響而顯示右側文字；改為在控制項層級明確清空 OnContent/OffContent，確保右側完全沒有文字。

### 待辦／已知限制
- 需以實際畫面確認開關右緣與預覽畫面在任何縮放比例下都保持一致。

---

## 2026-11-05 - v1.0.4

### 目標
修正即時預覽開關雖靠右、但實際開關軌道仍因 ToggleSwitch 預設最小寬度與內容間距而偏左的問題，同時保留「開啟／關閉」文字。

### 變更
- `RecorderPage`：將預覽狀態文字與 ToggleSwitch 拆成靠右的水平排列，文字置於開關左側。
- `ToggleSwitch` 設定 `MinWidth="0"` 與 `Margin="0,0,-12,0"`，補償範本右側固定間距，使開關軌道貼齊預覽畫面右緣。
- `RecorderViewModel`：新增 `LivePreviewStateText`，依預覽狀態顯示開啟／關閉。
- `Resources.resw`：新增 `LivePreviewOnText`、`LivePreviewOffText`。
- `README.md`、`CHANGELOG.md`、`RELEASE_NOTES.md`、版本檔：更新至 v1.0.4。

### 問題與解法
- ToggleSwitch 預設 `MinWidth=154`，且範本在開關右側保留 12px 內容欄位；僅設定 `HorizontalAlignment="Right"` 只會讓整塊控制項靠右，開關本體仍偏左。改以縮小最小寬度並補償右側間距解決。

### 待辦／已知限制
- 需實際啟動程式確認不同縮放比例下的 1px 以內對齊。

---

## 2026-11-05 - v1.0.3

### 目標
新增護眼黃／護眼綠主題、調整主題排序、修正淺色主題切換後畫面未刷新，並讓即時預覽開關貼齊預覽畫面右緣。

### 變更
- `AppThemePreference`、`ThemeService`、`Styles/Themes/EyeCareYellowTheme.xaml`、`EyeCareGreenTheme.xaml`：新增護眼黃與護眼綠兩個淺色主題。
- `SettingsViewModel`：主題順序調整為跟隨系統、淺色、深色、護眼黃、護眼綠、四套淺色、四套深色。
- `ThemeService`：修正自訂淺色與內建淺色／深色之間切換時，`RequestedTheme` 未變更導致 `ThemeResource` 未重新解析的問題。
- `RecorderPage`：即時預覽 ToggleSwitch 改為靠右對齊，並移除開關兩側文字，使開關貼齊預覽畫面右緣。
- `Resources.resw`：新增護眼主題文字，移除預覽開關的開啟／關閉文字。
- `README.md`、`CHANGELOG.md`、`RELEASE_NOTES.md`、版本檔：更新至 v1.0.3。

### 問題與解法
- 切換兩個同為淺色基底的主題時，僅替換調色盤不會觸發主題資源重算；改為在相同 `RequestedTheme` 下重新套用預設與目標主題，強制刷新。

### 待辦／已知限制
- 尚未針對所有螢幕 DPI 與高對比模式逐一切換驗證。

---

## 2026-11-05 - v1.0.2

### 目標
記住主視窗位置與大小、強化主題配色辨識度、改善導覽與錄影設定操作，並為錄影檔案庫補上重新命名功能。

### 變更
- `WindowPlacement`、`AppSettings`、`SettingsService`、`MainWindow`：新增主視窗位置、大小與最大化狀態的儲存與還原；視窗移出工作區時自動校正。
- `Styles/Themes/*.xaml`：加深並提高海洋藍、森林綠、暖陽橘、薰衣草紫的彩度與明暗對比。
- `MainWindow.xaml`：將「設定」移到主選單「快剪與複習」下方。
- `SettingsPage`：錄影倒數改為下拉選單，提供 0～30 秒、每 5 秒一個選項，預設 5 秒。
- `RecorderPage`、`EditorPage`：右欄寬度由 `1.7*` 縮小為 `1.5*`。
- `EditorPage`：PNG 快拍 Toast 改顯示於畫面上方。
- `LibraryPage`、`LibraryViewModel`：新增重新命名功能，同步重新命名 MP4 與 `.bookmarks.json`，包含檔名驗證與錯誤提示。
- `README.md`、`CHANGELOG.md`、`RELEASE_NOTES.md`、版本檔：更新至 v1.0.2。

### 問題與解法
- 視窗關閉事件為非同步時，若未先取消關閉可能來不及寫入設定；改為先 `args.Cancel = true`、儲存完成後再關閉。

### 待辦／已知限制
- 若錄影檔正在快剪編輯器中載入，重新命名後需重新載入該檔。

---

## 2026-11-05 - v1.0.1

### 目標
新增應用程式圖示、可開關的即時預覽與可設定的錄影倒數，擴充顯示主題與播放體驗，並改善快剪頁的 PNG 快拍回饋與操作便利性。

### 變更
- `Assets/AppIcon.ico`、`CourseRec.csproj`、`MainWindow`、`MiniOverlayWindow`：加入 exe／工作列／視窗圖示。
- `AppSettings`、`CourseRecDefaultOptions`、`SettingsService`、`appsettings.json`：新增 `RecordingCountdownSeconds`（0～30，預設 3）與 `IsLivePreviewEnabledByDefault`（預設關閉）。
- `SettingsPage`：顯示主題移至第一張卡片；新增「錄影設定」卡片（倒數秒數、預設啟用即時預覽）；「設定與關於」更名為「設定」。
- `AppThemePreference`、`ThemeService`、`Styles/Themes/*.xaml`：新增海洋藍、森林綠、暖陽橘、薰衣草紫 4 套完整配色（各含淺色／深色）。
- `RecorderPage`：即時預覽新增本次工作台開關（預設讀取設定值）；開始錄影按鈕依倒數秒數顯示；右欄寬度縮小。
- `EditorPage`：播放速度新增 `0.25x`、`0.5x`；新增播放器全螢幕切換、PNG 快拍 Toast 提示與開啟 PNG 資料夾按鈕；右欄寬度縮小。
- `Controls/ToastPresenter`：新增頁面底部浮動 Toast 控制項。

### 問題與解法
- WinUI 自訂主題無法只靠 `ElementTheme` 呈現多套配色，改以執行時合併 `ResourceDictionary` 至視窗根元素，並依 `RequestedTheme` 選擇 Light／Dark 調色盤。
- `SymbolIcon` 不支援 `Completed` 符號，改用 `Accept`。

### 待辦／已知限制
- 自訂主題的實際視覺效果仍需人工啟動程式逐套確認。
- 即時預覽開關僅保留本次執行期間，不寫回設定檔；預設值由設定頁控制。

---

## 2026-11-04 - v1.0.0

### 目標
完成 Phase 4 快剪編輯器、AI 音訊匯出與文件收尾：實作錄影檔案庫、`EditorPage` 倍速播放與書籤跳轉、`MediaComposition` 頭尾修剪／分段刪除／人聲音量放大、MP4 與 M4A 匯出及隨附書籤自動重對齊，並補齊所有發行文件。

### 變更
- `src/CourseRec/Services/Editing/`：實作 `MediaEditingService`（影片時長讀取、`MediaComposition.GetThumbnailAsync` 簡報頁 PNG 擷取、多段保留區間拼接、100%~200% 音量增強與 MP4 / M4A 純音訊雙格式匯出）。
- `src/CourseRec/ViewModels/LibraryViewModel.cs` & `Views/LibraryPage.xaml`：實作錄影卡片清單、一鍵進入快剪、開啟檔案位置與 `ContentDialog` 二次確認刪除。
- `src/CourseRec/ViewModels/EditorViewModel.cs` & `Views/EditorPage.xaml`：實作 `0.75x ~ 2.0x` 倍速播放、書籤點擊跳轉與新增、頭尾保留與多段剪除標記、未儲存變更防呆與雙格式匯出。
- `tests/CourseRec.Tests/EditorAndLibraryTests.cs`：新增檔案庫掃描／刪除與快剪分段匯出／書籤重對齊整合單元測試。
- `README.md`、`CHANGELOG.md`、`RELEASE_NOTES.md`、`Directory.Build.props`：升級至 `v1.0.0` 並補齊功能、建置與發行說明。

### 問題與解法
- 匯出純音訊 M4A 給 AI 逐字稿工具時需移除視訊軌 → 透過 `MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High)` 並明確設定 `profile.Video = null` 交由 `MediaComposition.RenderToFileAsync` 轉出純 AAC 音訊容器。

### 待辦／已知限制
- 尚未於實體 Windows 10 1809 舊版環境進行長時實機壓測；單一視窗獨立錄音於舊版 Windows 10 會依設計自動降級為系統全體聲音擷取。

---

## 2026-11-04 - v0.3.0

### 目標
完成 Phase 3 硬體編碼錄影與懸浮控制：實作 `IMFSinkWriter` H.264/AAC 硬體編碼、暫停／繼續時間軸扣除、定時自動停止、磁碟空間保護、全域快捷鍵、時間書籤與防入鏡迷你懸浮控制列。

### 變更
- `src/CourseRec/Interop/MediaFoundationNativeMethods.cs` & `Services/Encoding/`：實作 `Mp4EncoderService`（啟用硬體轉換與防節流、RGB32 等比例縮放與 PCM16/AAC 封裝）與 `RecordingTimelineClock` 暫停扣除計時器。
- `src/CourseRec/Services/Bookmarks/`：實作 `.bookmarks.json` 讀寫與多段重疊剪除區間合併、書籤時間戳平移演算法。
- `src/CourseRec/Interop/HotkeyNativeMethods.cs` & `Services/Hotkeys/`：透過 `SetWindowSubclass` 與 `RegisterHotKey` 實作四組全域快捷鍵管理。
- `src/CourseRec/ViewModels/RecorderViewModel.cs`、`Views/RecorderPage.xaml`、`Views/MiniOverlayWindow.xaml`：串接 3 秒倒數、等距無漂移影音寫入迴圈、雙軌 VU 表、最小化提示橫幅、快拍與懸浮視窗。
- `tests/CourseRec.Tests/TimelineAndBookmarkTests.cs`：新增暫停時間軸扣除、重疊剪除書籤平移、快捷鍵解析與 Letterbox 縮放測試。

### 問題與解法
- 長時間（數小時）15fps / 30fps 錄影若以單幀固定音訊樣本數累加易產生微秒級漂移 → 改以累計訊框商差 `((i+1)*48000/fps) - (i*48000/fps)` 精確計算每步音訊訊框數，實現零累積誤差。

### 待辦／已知限制
- 接續實作 Phase 4 錄影檔案庫、`EditorPage` 倍速播放與書籤跳轉、`MediaComposition` 剪輯與 MP4 / M4A 匯出。

---

## 2026-11-04 - v0.2.0

### 目標
完成 Phase 2 原生畫面與雙路音訊擷取：實作多螢幕／背景視窗枚舉、`Windows.Graphics.Capture` 預覽與 PNG 快拍、原生 WASAPI 系統／單一視窗 Process Loopback 與麥克風擷取、48kHz 雙軌混音器與 Soft Limiter 防爆音。

### 變更
- `src/CourseRec/Interop/`：新增 `CaptureNativeMethods`、`D3D11NativeMethods` 與 `WasapiNativeMethods`（含 `VAD\Process_Loopback` 非同步介面啟動與 `IAgileObject` 回呼）。
- `src/CourseRec/Services/Capture/`：實作 `CaptureEnumerationService`（過濾 Cloaked 與工具視窗）與 `ScreenCaptureService`（Staging Texture 影格複製、最小化狀態偵測、PNG 快拍存檔與剪貼簿複製）。
- `src/CourseRec/Services/Audio/`：實作 `AudioRingBuffer`、`DualTrackAudioMixer`（取樣率／聲道線性重取樣、即時雙 VU 峰值、0.85 Soft Limiter）與 `AudioCaptureService`（端點熱插拔偵測與舊版 Windows 自動降級）。
- `src/CourseRec/CourseRec.csproj` & `tests/CourseRec.Tests/CourseRec.Tests.csproj`：啟用 `WindowsAppSDKSelfContained` 並停用 `WindowsAppSdkBootstrapInitialize` 以確保無頭測試與未封裝模式穩定啟動。
- `tests/CourseRec.Tests/AudioMixerTests.cs`：新增環形緩衝區欠載補零／溢位保護、Soft Limiter 防破音、純聽課強制靜音與 44.1kHz→48kHz 重取樣單元測試。

### 問題與解法
- `CourseRec.Tests` 載入 WinAppSDK 時預設執行 `MddBootstrapInitialize` 導致無頭測試處理序等待 → 啟用 `WindowsAppSDKSelfContained=true` 並設定 `WindowsAppSdkBootstrapInitialize=false` 解決。
- `[UnmanagedCallersOnly]` 回呼不可直接由 C# 端呼叫（CS8901） → 拆分為內部輔助方法供 C# 與原生 COM vtable 共用。

### 待辦／已知限制
- 接續實作 Phase 3 `IMFSinkWriter` H.264/AAC 硬體編碼、暫停時間軸校正、全域快捷鍵、書籤記錄與錄製工作台完整串接。

---

## 2026-11-04 - v0.1.0

### 目標
完成 Phase 1 專案骨架與基礎建設：建立 `CourseRec.sln`、套件集中管理、`Microsoft.Extensions.Hosting` DI 啟動架構、設定讀寫服務、繁體中文 `.resw` 資源、淺深色主題資源字典與四大頁面導覽。

### 變更
- `Directory.Build.props` / `Directory.Packages.props`：設定 `net10.0-windows10.0.19041.0`、`x64`、`Nullable`、`TreatWarningsAsErrors` 與集中套件版本。
- `AGENTS.md`：補齊專案名稱、命名空間 `Com.VibeCode.CourseRec`、原生影音管線需求與系統架構限制。
- `src/CourseRec/Models/`：建立情境模式、畫質預設、擷取目標、時間書籤、剪輯區間與設定模型。
- `src/CourseRec/Strings/zh-Hant/Resources.resw` & `Helpers/LocalizationService.cs`：集中管理繁體中文 UI 文案，並支援桌面端與單元測試雙環境讀取。
- `src/CourseRec/Styles/`：建立淺色／深色主題資源字典與共用控制項樣式。
- `src/CourseRec/Services/Settings/`：實作 `%AppData%\CourseRec\settings.json` 非同步原子讀寫與 `Window.Content` 動態主題切換。
- `src/CourseRec/Views/` & `ViewModels/`：建立單一實例啟動、主視窗導覽與關閉防呆對話框、迷你懸浮視窗骨架與完整設定頁。
- `tests/CourseRec.Tests/SettingsServiceTests.cs`：新增設定序列化、畫質偶數對齊與多語系讀取單元測試。

### 問題與解法
- 未封裝模式（`WindowsPackageType=None`）與 `xUnit` 測試環境對 PRI 資源載入行為不同 → 於 `LocalizationService` 實作 `ResourceLoader` 優先、`.resw` XML 解析後備的雙軌讀取機制。

### 待辦／已知限制
- 接續實作 Phase 2 原生 `Windows.Graphics.Capture` 畫面預覽／快拍與 WASAPI 雙軌混音管線。
