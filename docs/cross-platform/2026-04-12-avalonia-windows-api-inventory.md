# Avalonia 主程序（`src/XIVLauncher`）Windows API 与平台专用行为清单

范围：`src/XIVLauncher` 下 C# 源码及 `XIVLauncher.csproj` 中的 COM 引用。不含 `XIVLauncher.Common*`（另有 `#if WIN32` 等，见 Common 专项）。

**说明：**「是否迁入 Common.Windows / Common.Unix」指 **逻辑与 P/Invoke 本体**；Avalonia 视图代码仍可留在主工程，通过调用抽象接口或 `Common.Windows` 中的静态助手完成平台工作。

---

## 1. 工程与包（整程序级）

| 项 | 位置 | 说明 | 建议 |
|---|------|------|------|
| `TargetFramework` `net10.0-windows10.0.19041.0` | `XIVLauncher.csproj` | 绑定 Windows 与 WinRT 面 | 多目标时拆出 `net10.0` + `net10.0-windows`，Hello/COM 仅后者 |
| `COMReference` `IWshRuntimeLibrary` | `XIVLauncher.csproj` | 桌面 `.lnk` 快捷方式 | 保留在 Windows TFM；创建逻辑可迁至 `Common.Windows` 助手类，主工程只调接口 |
| `AdysTech.CredentialManager` | `XIVLauncher.csproj` | Windows 凭据管理器 | 与 `CredentialManager.cs` 一并：**仅 Windows 程序集引用**；或迁入 `Common.Windows` |
| `System.Windows.Input`（`ICommand` 等） | 多个 ViewModel、`SyncCommand.cs` | 在 `net10.0-windows` 上可用；裸 `net10.0` 上常不可用 | **不必**进 Common；跨平台时改为 `CommunityToolkit.Mvvm` / Avalonia 社区惯例，或共享小接口在主工程内 |

---

## 2. P/Invoke（`user32` / `kernel32`）

| 功能 | 文件 | API | 建议归属 |
|------|------|-----|----------|
| 检测 Ctrl/Shift/Alt 修饰键 | `Windows/MainWindow.axaml.cs` | `GetAsyncKeyState` | **可留主工程**：与 Avalonia 窗口输入强相关；非 Windows 用 `#if` 关闭或改为 Avalonia 键盘 API |
| 将游戏进程窗口置前 | `AppUtil.cs` | `ShowWindow`, `SetForegroundWindow` | **迁入 `Common.Windows`** 为 `WindowsWindowActivation` 之类；Unix 可用 `xdotool`/X11（后期）或空实现 |
| 从任务切换器隐藏窗口、扩展样式 | `Xaml/HideFromWindowSwitcher.cs` | `GetWindowLong`/`SetWindowLong`/`SetWindowLongPtr`, `SetLastError` | **留主工程 + `#if WINDOWS`**：依赖 Avalonia 原生句柄；或抽象 `INativeWindowChrome` 由 Windows 实现放 `Common.Windows` |
| 多显示器窗口位置 | `Xaml/PreserveWindowPosition.cs` | `MonitorFromWindow`, `GetMonitorInfo` | 同上，**Win32 窗口行为**，优先 `#if` 或小型互操作类与视图同库 |
| Hello 弹窗置前 | `Accounts/Cred/CredProviders/WindowsHello.cs` | `FindWindow`, `SetForegroundWindow` | **随 `WindowsHello` 整体**：迁入 `Common.Windows` 或仅 `net10.0-windows` 子项目，避免 Linux 编译引用 WinRT |

---

## 3. 注册表（`Microsoft.Win32`）

| 功能 | 文件 | 说明 | 建议归属 |
|------|------|------|----------|
| 国服安装路径探测（卸载信息键） | `AppUtil.TryGamePaths` | HKLM Uninstall + 中文键名 | **抽象为 `IGameInstallPathProbe`**，实现放 **`Common.Windows`**；**`Common.Unix`** 提供 Steam 库/用户自选路径等实现 |
| Windows 版本展示 | `Windows/ChangelogWindow.axaml.cs` | `Registry.GetValue` → ReleaseId | **可选** `IOperatingSystemInfo`：Windows 读注册表，Unix 读 `RuntimeInformation.OSDescription` 或文件 |
| 兼容层 RUNASADMIN、GShade 安装信息 | `Game/ProblemCheck.cs` | HKCU/HKLM | **整类 Windows 专用**：`#if WINDOWS` 早返回或拆 **`ProblemCheckWindows`** 至 **`Common.Windows`**，主工程 `ProblemCheck.RunCheck` 内分发 |

---

## 4. COM / Shell（非注册表）

| 功能 | 文件 | 说明 | 建议归属 |
|------|------|------|----------|
| 创建/读取 `.lnk` | `FirstTimeSetupWindow.axaml.cs`, `AccountSwitcher.axaml.cs` | `IWshRuntimeLibrary` | **助手方法迁入 `Common.Windows`**（如 `WindowsShortcut.Create`），视图仍触发；非 Windows 分支跳过或创建 `.desktop`（若要做，放 **`Common.Unix`**） |

---

## 5. 进程启动（UAC / 提权 / 专用路径）

| 功能 | 文件 | 说明 | 建议归属 |
|------|------|------|----------|
| 启动 ArgReader 并 `Verb = "runas"` | `Game/RemoteArgReader.cs` | 依赖管理员与 `.exe` 名 | **严格 Windows + WeGame 链**：保留在主工程或 **`Common.Windows`**；非 Windows **不包含该代码路径** |
| 修复兼容标志、打开 GShade 安装包等 | `Game/ProblemCheck.cs` | `runas` + `cmd` / `%WINDIR%` | 随 ProblemCheck 拆分至 **Windows 实现** |
| `MainWindowViewModel` 内 `runas` | `Windows/ViewModel/MainWindowViewModel.cs` | 与补丁/修复流程相关 | 按调用点 **逐条 `#if` 或注入 `IElevatedProcessLauncher`**，实现放 **`Common.Windows`** |

---

## 6. 凭据与加密（Windows 专用 API）

| 功能 | 文件 | 说明 | 建议归属 |
|------|------|------|----------|
| Windows 凭据管理器封装 | `Accounts/Cred/CredProviders/CredentialManager.cs` | AdysTech → CredUI API | **迁入 `Common.Windows`**（或独立 `XIVLauncher.Windows.Security`），主工程 `AccountManager` 按 OS 解析类型 |
| Windows Hello | `Accounts/Cred/CredProviders/WindowsHello.cs` | WinRT + user32 | **仅 `net10.0-windows` 编译**；物理文件可迁 **`Common.Windows`** 并条件包含 |
| 无用 using | `Accounts/XivAccount.cs` | `using AdysTech.CredentialManager` 未使用 | **删除**即可 |

---

## 7. 其他 Windows 专用行为（非 P/Invoke）

| 功能 | 文件 | 说明 | 建议归属 |
|------|------|------|----------|
| 文件占用检测 | `AppUtil.TryYellOnGameFilesBeingOpen` | 已用 **`WindowsRestartManager`**（在 **Common.Windows**） | **逻辑可留主工程**；若希望主工程零 Windows 引用，可迁 **`Common.Windows`** 并传 `Window` 作父窗口（仍依赖 UI） |
| `explorer.exe /select` | `Support/PackGenerator.cs` | 资源管理器高亮文件 | **`IFileManagerReveal`**：Windows 用 explorer，macOS `open -R`，Linux `xdg-open` 目录（**`Common.Unix`** 或主工程分派） |
| `Win32Exception` 文案 | `MainWindowViewModel.cs` 等 | SmartScreen 等 | **保留在 Windows 分支**；Unix 不会命中 ArgReader 同路径 |
| `ProcessWindowStyle.Hidden` 等 | `RemoteArgReader.cs` | Win32 进程创建细节 | 随 ArgReader |

---

## 8. 已跨平台或需注意但不必迁入 Common 的项

| 项 | 说明 |
|----|------|
| `Process.Start(..., UseShellExecute = true)` 打开浏览器/文件夹 | 在 Linux/macOS 通常可用；个别 URL 需测；**不必**迁入 Common |
| **Velopack**（`App.axaml.cs`, `Updates.cs`） | 自身支持多平台；保留主工程，按 RID 打包 |
| **`System.Management` using**（`AppUtil.cs`） | 当前未见使用，**可删 using** 减少误导 |

---

## 9. 分层原则小结

| 迁入 **Common.Windows** | 迁入 **Common.Unix** | 留在 **XIVLauncher**（常带 `#if` 或接口） |
|-------------------------|----------------------|----------------------------------------|
| 注册表游戏路径探测、ProblemCheck 注册表逻辑 | 对应路径探测、`.desktop`、XDG（若做） | Avalonia 窗口 + Win32 样式（HideFromSwitcher、PreservePosition）、视图事件绑定 |
| 提权启动、ArgReader 启动链（若希望共享给工具） | Wine/Steam 相关已由现有 Unix 项目覆盖部分 | ViewModel、页面导航、`ICredProvider` 注册表（可委托给 Common） |
| 凭据管理器、Windows Hello 实现 | KeySharp 等 **Phase 2** 在 Core 已有参考 | `AccountManager` 编排、SQLite、设置 UI |

**不必**把纯 UI 或强依赖 `Avalonia.Controls.Window` 的代码硬塞进 Common；应通过 **小接口**（如 `IGameInstallPathProbe`、`IFileManagerReveal`）让 Common 只做「无 UI 或可选 parent 句柄」的平台实现。

---

## 10. 参考

- [2026-04-12-avalonia-cross-platform-design.md](./2026-04-12-avalonia-cross-platform-design.md)
- [plans/2026-04-12-cross-platform-phase2-windows-api.md](./plans/2026-04-12-cross-platform-phase2-windows-api.md)（细化任务）
