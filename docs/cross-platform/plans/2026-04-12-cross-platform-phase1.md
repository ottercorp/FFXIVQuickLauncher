# Cross-Platform Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成跨平台落地的第一阶段：PatchInstaller 平台中立、`XIVLauncher.Common` 的 `WIN32`/`LINUX`/`OSX` 编译常量与 **目标 RID** 对齐，并在主程序中接入 `UnixSteam` 的运行时选择（与 [设计文档](../2026-04-12-avalonia-cross-platform-design.md) 一致）。

**Architecture:** 补丁子进程改为 `net10.0` CLI；Common 在带 `-r` 的发布构建上按 RID 定义预处理符号，无 RID 时回退到构建主机 OS（本地开发）；`App.axaml.cs` 用 `Environment.OSVersion.Platform` 或 `OperatingSystem.IsWindows()` 在 `WindowsSteam` 与 `UnixSteam` 之间选择。

**Tech stack:** .NET 10, MSBuild, Serilog, existing `XIVLauncher.Common.*` projects.

**Spec:** [2026-04-12-avalonia-cross-platform-design.md](../2026-04-12-avalonia-cross-platform-design.md) §4、§7、§8；[patch-installer.md](../patch-installer.md)。

---

### Task 1: PatchInstaller — 去掉 WPF / `net10.0-windows`

**Files:**
- Modify: `src/XIVLauncher.PatchInstaller/XIVLauncher.PatchInstaller.csproj`
- Modify: `src/XIVLauncher.PatchInstaller/Commands/RpcCommand.cs`

- [ ] **Step 1: 更新工程文件**

将 `XIVLauncher.PatchInstaller.csproj` 中 `TargetFramework` 改为 `net10.0`，删除 `<UseWPF>true</UseWPF>`。将 WPF 资源项改为非 WPF 形式（若 `Resources/` 下仅有图标，可用 `<None>` 或 `<Content>`，并仅在需要 Windows 可执行图标时用条件 `PropertyGroup` 保留 `ApplicationIcon`）。

- [ ] **Step 2: 替换 RpcCommand 中的 MessageBox**

在 `RpcCommand.cs` 删除 `using System.Windows;`，将 `catch` 中的 `MessageBox.Show` 改为记录日志并重新抛出，例如：

```csharp
        catch (Exception ex)
        {
            Log.Fatal(ex, "Patcher init failed.");
            throw;
        }
```

确保 `catch` 前已在 `Handle()` 内配置好 `Log.Logger`（当前代码在 `try` 之前已配置，满足要求）。

- [ ] **Step 3: 构建 PatchInstaller**

运行：`dotnet build src/XIVLauncher.PatchInstaller/XIVLauncher.PatchInstaller.csproj -c Release`  
预期：成功，无对 `PresentationFramework` 的引用。

- [ ] **Step 4: 提交**

```bash
git add src/XIVLauncher.PatchInstaller/XIVLauncher.PatchInstaller.csproj src/XIVLauncher.PatchInstaller/Commands/RpcCommand.cs
git commit -m "build(PatchInstaller): target net10.0 and remove WPF MessageBox"
```

---

### Task 2: XIVLauncher.Common — 按 RID（及无 RID 时按主机）定义 WIN32 / LINUX / OSX

**Files:**
- Modify: `src/XIVLauncher.Common/XIVLauncher.Common.csproj`

- [ ] **Step 1: 用 RID 优先的常量替换仅按主机 OS 的 PropertyGroup**

将当前基于 `IsWindows`/`IsOSX`/`IsLinux`（构建机）且直接设置 `DefineConstants` 的块，改为：

1. 保留主机检测，但重命名为 `IsWindowsHost`、`IsOSXHost`、`IsLinuxHost`（或等价命名）。
2. 当 `$(RuntimeIdentifier)` **非空** 时：
   - `RuntimeIdentifier` 以 `win` 开头 → 追加 `WIN32`
   - 以 `osx` 或 `linux` 开头 → 分别追加 `OSX`（macOS 可继续追加 `WINE_XIV_MACOS` 若仍需要）或 `LINUX`
3. 当 `$(RuntimeIdentifier)` **为空** 时，沿用主机检测回退（与今日本地 `dotnet build` 行为接近）。

MSBuild 示例（需按仓库现有 `WINE_XIV_MACOS` 规则微调）：

```xml
    <PropertyGroup>
        <IsWindowsHost Condition="'$([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform($([System.Runtime.InteropServices.OSPlatform]::Windows)))' == 'true'">true</IsWindowsHost>
        <IsOSXHost Condition="'$([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform($([System.Runtime.InteropServices.OSPlatform]::OSX)))' == 'true'">true</IsOSXHost>
        <IsLinuxHost Condition="'$([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform($([System.Runtime.InteropServices.OSPlatform]::Linux)))' == 'true'">true</IsLinuxHost>
    </PropertyGroup>

    <PropertyGroup Condition="'$(RuntimeIdentifier)' != '' and $(RuntimeIdentifier.StartsWith('win'))">
        <DefineConstants>$(DefineConstants);WIN32</DefineConstants>
    </PropertyGroup>
    <PropertyGroup Condition="'$(RuntimeIdentifier)' != '' and $(RuntimeIdentifier.StartsWith('osx'))">
        <DefineConstants>$(DefineConstants);OSX;WINE_XIV_MACOS</DefineConstants>
    </PropertyGroup>
    <PropertyGroup Condition="'$(RuntimeIdentifier)' != '' and $(RuntimeIdentifier.StartsWith('linux'))">
        <DefineConstants>$(DefineConstants);LINUX</DefineConstants>
    </PropertyGroup>

    <PropertyGroup Condition="'$(RuntimeIdentifier)' == '' and '$(IsWindowsHost)'=='true'">
        <DefineConstants>$(DefineConstants);WIN32</DefineConstants>
    </PropertyGroup>
    <PropertyGroup Condition="'$(RuntimeIdentifier)' == '' and '$(IsOSXHost)'=='true'">
        <DefineConstants>$(DefineConstants);OSX;WINE_XIV_MACOS</DefineConstants>
    </PropertyGroup>
    <PropertyGroup Condition="'$(RuntimeIdentifier)' == '' and '$(IsLinuxHost)'=='true'">
        <DefineConstants>$(DefineConstants);LINUX</DefineConstants>
    </PropertyGroup>
```

- [ ] **Step 2: 验证交叉语义**

在 Windows 上执行：`dotnet build src/XIVLauncher.Common/XIVLauncher.Common.csproj -c Release -r linux-x64`  
在 Linux 上执行（若可用）：`dotnet build ... -r win-x64`  
预期：`WIN32` 相关 `#if` 块在 **目标为 linux-x64 的 Common 输出** 中不应启用（可通过检查生成的引用程序集或临时 `#error WIN32` 探针验证，任选一种团队认可的方式）。

- [ ] **Step 3: 提交**

```bash
git add src/XIVLauncher.Common/XIVLauncher.Common.csproj
git commit -m "build(Common): define platform constants from RuntimeIdentifier with host fallback"
```

---

### Task 3: XIVLauncher 主工程 — 引用 Common.Unix 与运行时 Steam 选择

**Files:**
- Modify: `src/XIVLauncher/XIVLauncher.csproj`
- Modify: `src/XIVLauncher/App.axaml.cs`

- [ ] **Step 1: 添加对 Common.Unix 的项目引用**

在 `XIVLauncher.csproj` 的 `<ItemGroup>` 中 `ProjectReference` 加入：

```xml
        <ProjectReference Include="..\XIVLauncher.Common.Unix\XIVLauncher.Common.Unix.csproj" />
```

（若与 Windows 专用引用产生重复传递引用冲突，按构建错误调整；通常与现有 `Common.Windows` 并存即可。）

- [ ] **Step 2: 将 `Steam` 类型改为接口并实现运行时分支**

把 `App.axaml.cs` 中 `public static WindowsSteam Steam` 改为 `public static ISteam Steam`（或保持具体类型但统一为 `ISteam` 字段，与 `Launcher` 构造函数一致）。在初始化处（当前约 `Steam = new WindowsSteam();`）替换为：

```csharp
                Steam = Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? new WindowsSteam()
                    : new UnixSteam();
```

所需 `using`：`XIVLauncher.Common.PlatformAbstractions`、`XIVLauncher.Common.Windows`、`XIVLauncher.Common.Unix`。

- [ ] **Step 3: 编译主工程（Windows 上现有 TFM）**

运行：`dotnet build src/XIVLauncher/XIVLauncher.csproj -c Release`  
预期：成功；若出现仅 Unix 程序集在 Windows 上不应执行的警告，可后续再收紧引用条件（本阶段以能通过 Windows CI 为主）。

- [ ] **Step 4: 提交**

```bash
git add src/XIVLauncher/XIVLauncher.csproj src/XIVLauncher/App.axaml.cs
git commit -m "feat: reference Common.Unix and select ISteam by OS"
```

---

## 计划自检（对照设计文档）

| 设计章节 | 本阶段任务 |
|----------|------------|
| §4 PatchInstaller / net10.0 | Task 1 |
| §4 Common `WIN32` 与 RID | Task 2 |
| §3 运行时选 Steam | Task 3 |
| §5 密钥 KeySharp | 留待 Phase 3 |
| §6 WeGame 仅 Windows | 留待后续任务（UI 与打包裁剪） |
| §7 Unix 内进程补丁 | 留待 Phase 3（主窗口/补丁调用点） |
| Windows API 清点与 Common 迁移 | [Phase 2 — Windows API](./2026-04-12-cross-platform-phase2-windows-api.md) |

---

**Plan complete and saved to `docs/cross-platform/plans/2026-04-12-cross-platform-phase1.md`. Two execution options:**

1. **Subagent-Driven (recommended)** — 每个任务派生子代理，任务间复核，迭代快。需使用 superpowers:subagent-driven-development。

2. **Inline Execution** — 本会话内按步骤执行，使用 superpowers:executing-plans 与检查点。

**Which approach?**（由维护者在开始实现时选定。）
