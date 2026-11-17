# CourseRec 建置與發行

本文件說明開發環境、建置、測試、非 Self-Contained 發行與 GitHub Release 流程。

---

## 1. 開發環境

| 工具 | 版本 |
| :--- | :--- |
| .NET SDK | 10.0.x |
| OS | Windows 10 1809 以上 |
| IDE | Visual Studio 2022/2026（含 Windows App SDK 元件）或 VS Code |
| Git | 2.x |

```powershell
dotnet --version
git --version
```

套件版本集中於 `Directory.Packages.props`，請勿在 `*.csproj` 寫死版本。

---

## 2. 建置與測試

```powershell
# 還原與建置（Debug x64）
dotnet build CourseRec.sln

# 單元測試
dotnet test tests/CourseRec.Tests/CourseRec.Tests.csproj

# 格式檢查
dotnet format CourseRec.sln --verify-no-changes --no-restore
```

需求：

- `Nullable`、`ImplicitUsings`、`TreatWarningsAsErrors` 皆啟用。
- 建置需 0 警告、0 錯誤。
- 測試失敗或格式差異不得發行。

---

## 3. 非 Self-Contained 發行

此模式**不包含 .NET 執行階段**，目標機器需安裝 .NET 10 Desktop Runtime；Windows App SDK 以 `WindowsAppSDKSelfContained=true` 隨附，無需另裝 Windows App Runtime。

### 3.1 x64

```powershell
dotnet publish src/CourseRec/CourseRec.csproj -c Release -r win-x64 --self-contained false `
  -p:Platform=x64 `
  -p:WindowsAppSDKSelfContained=true `
  -p:WindowsPackageType=None `
  -o artifacts/publish/win-x64
```

### 3.2 ARM64

```powershell
dotnet publish src/CourseRec/CourseRec.csproj -c Release -r win-arm64 --self-contained false `
  -p:Platform=ARM64 `
  -p:PlatformTarget=ARM64 `
  -p:WindowsAppSDKSelfContained=true `
  -p:WindowsPackageType=None `
  -o artifacts/publish/win-arm64
```

> `Directory.Build.props` 預設 `PlatformTarget=x64`，ARM64 發行必須同時覆寫 `Platform` 與 `PlatformTarget`。

### 3.3 檢查產物

```powershell
Get-ChildItem artifacts/publish/win-x64 | Select-Object Name, Length
Get-ChildItem artifacts/publish/win-arm64 | Select-Object Name, Length
```

必要檔案：

- `Com.VibeCode.CourseRec.exe`
- `Com.VibeCode.CourseRec.dll`
- `Com.VibeCode.CourseRec.runtimeconfig.json`
- `Com.VibeCode.CourseRec.deps.json`
- `Com.VibeCode.CourseRec.pri`
- `appsettings.json`
- `Microsoft.WinUI.dll`
- `Microsoft.WindowsAppRuntime.Bootstrap.dll` 與 Windows App SDK 原生 DLL
- 其他相依組件

確認 `runtimeconfig.json` 的 `includedFrameworks`／`framework` 符合非 Self-Contained 設定。

---

## 4. 壓縮 Release 資產

於專案根目錄執行：

```powershell
$version = "1.5.5"
$stage = "artifacts/stage"

foreach ($rid in @("win-x64", "win-arm64")) {
    $target = Join-Path $stage $rid
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Path $target -Force | Out-Null

    Copy-Item "artifacts/publish/$rid/*" $target -Recurse -Force
    Get-ChildItem $target -Filter *.pdb -Recurse | Remove-Item -Force
    Copy-Item "LICENSE" $target -Force
    Copy-Item "README.md" $target -Force

    $zip = "artifacts/CourseRec-$version-$rid-framework-dependent.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$target/*" -DestinationPath $zip -CompressionLevel Optimal
}

Get-ChildItem artifacts/*.zip | Select-Object Name, Length
```

壓縮檔命名固定為：

```text
CourseRec-<版本>-win-x64-framework-dependent.zip
CourseRec-<版本>-win-arm64-framework-dependent.zip
```

---

## 5. 版本管理

版本集中於 `Directory.Build.props`：

```xml
<Version>1.5.5</Version>
<AssemblyVersion>1.5.0.0</AssemblyVersion>
<FileVersion>1.5.5.0</FileVersion>
```

規則：

- `Version` 採 `主.次.修`。
- 功能變更遞增版本；`AssemblyVersion` 僅主次版變更時調整。
- 同步更新 `CHANGELOG.md`、`RELEASE_NOTES.md` 與 `docs/DEV_LOG.md`。
- Git tag 使用 `v<版本>`，例如 `v1.5.5`。

---

## 6. GitHub Release 流程

```powershell
# 確認遠端與狀態
git status
git log --oneline -5

# 建立並推送標籤
git tag v1.5.5
git push origin main
git push origin v1.5.5

# 建立 Release 並上傳資產
gh release create v1.5.5 `
  artifacts/CourseRec-1.5.5-win-x64-framework-dependent.zip `
  artifacts/CourseRec-1.5.5-win-arm64-framework-dependent.zip `
  --title "CourseRec v1.5.5" `
  --notes-file RELEASE_NOTES.md
```

若 Release 已存在，改用：

```powershell
gh release upload v1.5.5 `
  artifacts/CourseRec-1.5.5-win-x64-framework-dependent.zip `
  artifacts/CourseRec-1.5.5-win-arm64-framework-dependent.zip `
  --clobber
```

---

## 7. 發行檢查清單

- [ ] `dotnet build` 0 警告、0 錯誤
- [ ] `dotnet test` 全數通過
- [ ] `dotnet format --verify-no-changes` 通過
- [ ] 版本號與 CHANGELOG／RELEASE_NOTES 一致
- [ ] x64 與 ARM64 皆完成 publish
- [ ] zip 內含執行檔、相依 DLL、`appsettings.json`、LICENSE 與 README
- [ ] 未包含 `.pdb`、`TestResults`、`bin`／`obj`
- [ ] README 下載連結與版本一致
- [ ] Release 說明包含系統需求與非 Self-Contained 提示

---

## 8. 常見問題

| 問題 | 解法 |
| :--- | :--- |
| `NETSDK1032`：RID 與 PlatformTarget 不相容 | ARM64 發行需加 `-p:PlatformTarget=ARM64` |
| 啟動提示缺少 .NET | 目標機器未安裝 .NET 10 Desktop Runtime |
| 啟動提示找不到 Windows App SDK | 確認發行時 `WindowsAppSDKSelfContained=true` |
| zip 過大 | 確認未誤用 `--self-contained true`；必要時排除 `.pdb` |
| 測試因 x64 平台失敗 | 使用 `-p:Platform=x64` 或依方案預設組態執行 |
