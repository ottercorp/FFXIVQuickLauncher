# Phase 2 — Windows API 清点、抽象与 Common 迁移（详细计划）

> **For agentic workers:** 建议配合 superpowers:subagent-driven-development 或 executing-plans 按任务顺序执行。步骤使用 `- [ ]` 勾选。

**Goal:** 在 Avalonia 主程序支持 `net10.0`（Linux/macOS）构建的前提下，将 **Windows 专用 API** 限制在 **`net10.0-windows` 条件编译** 或 **`XIVLauncher.Common.Windows`**（及未来的 Unix 对等实现）中，避免 Linux 编译时解析到 `user32`、`Registry`、COM、WinRT。

**前置条件：** [2026-04-12-cross-platform-phase1.md](./2026-04-12-cross-platform-phase1.md) 已完成（多 RID Common 常量、Steam 运行时选择、PatchInstaller 中立化）。

**清单全文（逐项理由与建议归属）：** [../2026-04-12-avalonia-windows-api-inventory.md](../2026-04-12-avalonia-windows-api-inventory.md)

---

## 文件结构预览（本阶段结束后期望状态）

| 区域 | 变更 |
|------|------|
| `XIVLauncher.Common/PlatformAbstractions/` | 新增 `IGameInstallPathProbe`、`IFileManagerReveal`（或等价命名）；仅接口与无 OS 的默认实现（可选） |
| `XIVLauncher.Common.Windows/` | 新增 `WindowsGameInstallPathProbe`（从 `AppUtil.TryGamePaths` 抽出注册表部分）、`WindowsFileManagerReveal`（explorer `/select`）、可选 `WindowsElevatedProcess` |
| `XIVLauncher.Common.Unix/` | 新增 `UnixGameInstallPathProbe`（Steam 默认路径扫描 + 用户配置回退，可复用 `ISteam` 游戏目录） |
| `src/XIVLauncher/Accounts/Cred/CredProviders/` | `CredentialManager.cs`、`WindowsHello.cs` **移动**至 `Common.Windows` 下子文件夹，或改为 `partial` + 链接文件仅在 windows TFM 编译 |
| `src/XIVLauncher` | `AppUtil`、`ProblemCheck`、`RemoteArgReader`、`PackGenerator`、部分 ViewModel **删除直接 P/Invoke/注册表** 或包在 `#if WINDOWS` 内 |

---

### Task Group A：主工程多目标与引用隔离

**Files:**
- Modify: `src/XIVLauncher/XIVLauncher.csproj`
- Modify: `src/XIVLauncher/App.axaml.cs`（Velopack、Hello 等）
- Modify: `src/XIVLauncher/Accounts/AccountManager.cs`

- [ ] **A1：将 `XIVLauncher` 改为 `TargetFrameworks`：`net10.0;net10.0-windows10.0.19041.0`**

  - `net10.0`：不引用 `COMReference`；不引用 `AdysTech.CredentialManager`（用 `ItemGroup` `Condition="'$(TargetFramework)' == 'net10.0-windows10.0.19041.0'"`）。
  - `OutputType`：非 Windows 使用 `Exe`；Windows 可保持 `WinExe`（可用条件 PropertyGroup）。
  - `RuntimeIdentifier`：从固定 `win-x64` 改为按发布矩阵指定，或开发期不设 RID。

- [ ] **A2：`WindowsHello` / `CredentialManager` 类型解析**

  - `AccountManager.GetCredProvider`：在 `net10.0` 上 **不得** 引用 `WindowsHello`、`AdysTech`；`CredType.WindowsHello` / `WindowsCredManager` 映射到 **抛出不支持** 或 **回落 NoCred** 并打日志。
  - 将两个 Provider 类物理移动到 `src/XIVLauncher.Common.Windows/Credentials/`（示例路径），命名空间 `XIVLauncher.Common.Windows.Credentials`，主工程添加 `using` 仅在 windows TFM 的代码文件中引用（或用 `InternalsVisibleTo` 避免——优先公开类）。

- [ ] **A3：验证**

  - `dotnet build src/XIVLauncher/XIVLauncher.csproj -f net10.0`
  - `dotnet build src/XIVLauncher/XIVLauncher.csproj -f net10.0-windows10.0.19041.0`

---

### Task Group B：游戏安装路径探测抽象

**Files:**
- Create: `src/XIVLauncher.Common/PlatformAbstractions/IGameInstallPathProbe.cs`
- Create: `src/XIVLauncher.Common.Windows/WindowsGameInstallPathProbe.cs`（从 `AppUtil.TryGamePaths` 迁移注册表逻辑）
- Create: `src/XIVLauncher.Common.Unix/UnixGameInstallPathProbe.cs`（最小实现：常见 Steam 路径 + 空回退）
- Modify: `src/XIVLauncher/AppUtil.cs`
- Modify: `src/XIVLauncher/Windows/FirstTimeSetupWindow.axaml.cs`（或调用方）

- [ ] **B1：定义接口**

```csharp
namespace XIVLauncher.Common.PlatformAbstractions;

public interface IGameInstallPathProbe
{
    /// <summary>Best-effort default game root; may not exist.</summary>
    string GetSuggestedGamePath();
}
```

- [ ] **B2：实现 `WindowsGameInstallPathProbe`**：复制 `AppUtil.TryGamePaths` 内 **foreach registryView** 整块及 `GetDefaultPath`/`GetCommonPaths` 中 **仅 Windows 合理** 的部分（`ProgramFilesX86`、注册表键）。保持与现网相同键名与回退顺序。

- [ ] **B3：实现 `UnixGameInstallPathProbe`**：使用 `UnixSteam` 或静态路径表列出 `steamapps/common/FINAL FANTASY XIV*` 等；若无则返回空或用户主目录占位（与设计文档「用户自选」一致）。

- [ ] **B4：`AppUtil.TryGamePaths` 变为一行分发**：`App.Services.GetRequiredService<IGameInstallPathProbe>().GetSuggestedGamePath()` 或静态工厂 `PlatformService.GameInstallPathProbe`（按团队 DI 习惯）。

---

### Task Group C：ProblemCheck 与 RemoteArgReader

**Files:**
- Modify: `src/XIVLauncher/Game/ProblemCheck.cs`
- Modify: `src/XIVLauncher/Game/RemoteArgReader.cs`
- Optional Create: `src/XIVLauncher.Common.Windows/WindowsProblemCheck.cs`

- [ ] **C1：`ProblemCheck.RunCheck` 入口** 首行：`if (!OperatingSystem.IsWindows()) return;`（或 `#if`）。确保 **非 Windows 不打开注册表**。

- [ ] **C2（可选加深）**：将 `ProblemCheck` 内 **所有** 注册表与 `runas` 逻辑剪切到 `Common.Windows/WindowsProblemCheck.cs`，`ProblemCheck.RunCheck` 仅 `WindowsProblemCheck.Run(parentWindow)`。

- [ ] **C3：`RemoteArgReader`**：整个类用 `#if WINDOWS` 包裹，或文件改名为 `RemoteArgReader.Windows.cs` + 条件包含；非 Windows 提供 **存根** `RemoteArgReader` 在 `Game/RemoteArgReader.Stub.cs` 中 `Start()` 直接 `Task.FromException(new PlatformNotSupportedException())`。所有 `MainWindowViewModel` 调用点在编译期或运行期 **禁止在 Linux 走 WeGame 分支**（与设计一致）。

---

### Task Group D：Shell 快捷方式与资源管理器

**Files:**
- Create: `src/XIVLauncher.Common.Windows/Shell/WindowsShortcut.cs`（静态方法 `Create`, `GetTarget`）
- Modify: `src/XIVLauncher/Windows/FirstTimeSetupWindow.axaml.cs`
- Modify: `src/XIVLauncher/Windows/AccountSwitcher.axaml.cs`
- Modify: `src/XIVLauncher/XIVLauncher.csproj`（COM 引用仅 windows TFM）
- Create: `src/XIVLauncher.Common/PlatformAbstractions/IFileManagerReveal.cs`
- Create: `src/XIVLauncher.Common.Windows/WindowsFileManagerReveal.cs`
- Create: `src/XIVLauncher.Common.Unix/UnixFileManagerReveal.cs`（`xdg-open` 目录 + `Process.Start`）
- Modify: `src/XIVLauncher/Support/PackGenerator.cs`

- [ ] **D1：抽出 `WindowsShortcut`**，替换两处 `IWshRuntimeLibrary` 直接调用；`FirstTimeSetup` 内创建桌面快捷方式仅在 `OperatingSystem.IsWindows()` 执行。

- [ ] **D2：`PackGenerator`** 使用 `IFileManagerReveal.RevealInFolder(path)` 替代硬编码 `explorer.exe`。

---

### Task Group E：窗口级 P/Invoke（Avalonia 句柄）

**Files:**
- Modify: `src/XIVLauncher/Windows/MainWindow.axaml.cs`
- Modify: `src/XIVLauncher/Xaml/HideFromWindowSwitcher.cs`
- Modify: `src/XIVLauncher/Xaml/PreserveWindowPosition.cs`
- Modify: `src/XIVLauncher/AppUtil.cs`（`BringProcessMainWindowToFront`）

- [ ] **E1：`MainWindow.axaml.cs` 中 `GetAsyncKeyState`**：用 `#if WINDOWS` 包裹；非 Windows 分支 `GetCurrentKeyModifiers` 返回 `KeyModifiers.None` 或改用 Avalonia `KeyEvents`（若已实现全局监听则删除 P/Invoke）。

- [ ] **E2：`HideFromWindowSwitcher` / `PreserveWindowPosition`**：`AttachedProperty` 在 `net10.0` 上 **no-op**（不调用 user32），避免 P/Invoke 链接。

- [ ] **E3：`AppUtil.BringProcessMainWindowToFront`**：实现移至 `Common.Windows` 静态类；`AppUtil` 内 `if (!OperatingSystem.IsWindows()) return;` 后调用。

---

### Task Group F：杂项清理与 ViewModel 提权

**Files:**
- Modify: `src/XIVLauncher/Windows/ChangelogWindow.axaml.cs`
- Modify: `src/XIVLauncher/Accounts/XivAccount.cs`
- Modify: `src/XIVLauncher/AppUtil.cs`（删除未用 `System.Management`）
- Modify: `src/XIVLauncher/Windows/ViewModel/MainWindowViewModel.cs`（所有 `Verb = "runas"`）

- [ ] **F1：`ChangelogWindow`**：注册表读 ReleaseId 包在 `OperatingSystem.IsWindows()`；否则显示 `Environment.OSVersion.VersionString` 或 `RuntimeInformation.OSDescription`。

- [ ] **F2：删除 `XivAccount.cs` 中无用 `using AdysTech.CredentialManager`。**

- [ ] **F3：`MainWindowViewModel` 中 `runas`**：提取方法 `TryStartElevated(ProcessStartInfo psi)`，内部 `#if WINDOWS`；非 Windows 显示本地化错误「不支持」或走非提权路径。

---

### Task Group G：`System.Windows.Input` / ICommand（若 `net10.0` 编译失败）

**Files:**
- Modify: 所有 `using System.Windows.Input` 的文件

- [ ] **G1：** 在 `dotnet build -f net10.0` 报错时，将 `ICommand` 替换为 `CommunityToolkit.Mvvm.Input.RelayCommand` 或 Avalonia 推荐的命令类型；**不必**迁入 Common。

---

## 自检（对照 [inventory](../2026-04-12-avalonia-windows-api-inventory.md)）

- [ ] 表中 §2 P/Invoke 均已：`#if`、迁 Common.Windows，或 Unix no-op。
- [ ] §3 注册表：仅 Windows 实现类或 `#if`。
- [ ] §4 COM：仅 Windows TFM + 助手在 Common.Windows。
- [ ] §5 提权：无 Linux 误调用。
- [ ] §6 凭据：Linux 编译无 AdysTech/WinRT。
- [ ] §7 `explorer`：已抽象 `IFileManagerReveal`。

---

## 与 Phase 3 的边界

- **KeySharp / Linux 密钥环 `ICredProvider`**、**Unix 默认内进程补丁**：见总体设计 §5、§7；本 Phase 2 以 **「Linux 能编过、Windows 行为不退化」** 为主，密钥环可作为 Phase 3 首包。

---

**计划文件：** `docs/cross-platform/plans/2026-04-12-cross-platform-phase2-windows-api.md`
