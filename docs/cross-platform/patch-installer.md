# XIVLauncher.PatchInstaller 与跨平台

## 结论

`XIVLauncher.PatchInstaller` 是 **纯命令行** 程序（`System.CommandLine`），**没有** XAML 或 WPF 窗口。工程里启用 WPF 与 `net10.0-windows` **并非业务必需**，属于历史配置。

## 代码层面的 Windows 专用点

| 位置 | 内容 |
|------|------|
| `XIVLauncher.PatchInstaller.csproj` | `TargetFramework` 为 `net10.0-windows`、`UseWPF` 为 `true`、WPF 风格 `<Resource Include="Resources/*.*" />`、可选 `ApplicationIcon` |
| `Commands/RpcCommand.cs` | 初始化失败时调用 `System.Windows.MessageBox.Show` |

其余命令与逻辑依赖 `XIVLauncher.Common` 中的补丁/RPC 实现，与 UI 框架无关。

## 跨平台化改动要点（已记录）

1. **目标框架**：改为 `net10.0`（或按需使用 `TargetFrameworks` 多目标；非 Windows 目标不需要 WinRT/WPF）。
2. **工程**：移除 `UseWPF`；资源按需要改为普通 `Content` / `None` / `EmbeddedResource`；Windows 发布若需要图标，可用条件 `PropertyGroup` 保留 `ApplicationIcon`。
3. **RpcCommand**：去掉对 `System.Windows` 的引用；失败路径改为 **`Serilog` + 控制台/标准错误**（或仅写日志），保持与 `Program.cs` 其它命令一致的无头行为。

## 验证说明

上述组合 **已在 Linux 环境中实际使用过**；本文档仅作设计/迁移记录，**不要求**在此处重复执行构建验证。

## 与主启动器的关系

主程序在 Unix 上亦可选择 **内进程补丁**（`external: false`），不依赖单独拉起 `XIVLauncher.PatchInstaller` 进程。独立 PatchInstaller 进程仍适用于希望 **进程隔离** 或与 Windows 侧既有 RPC 流程对齐的场景；无论是否外置，PatchInstaller 本身都 **不必** 绑定 WPF。
