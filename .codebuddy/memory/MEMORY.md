# SuperDucker 项目长期记忆

## 项目约定（Conventions）

- **README 版本记录必须倒序**：最新版本（如 1.1.0）在上，旧版本在下。每次更新版本后保持倒序，不要把最新版追加到表格末尾。

## 技术栈与约束

- WPF (.NET 8) 桌面应用，含控制台子项目 `SuperDucker.Cli` 与共享库 `SuperDucker.Shared`。
- **不能用 AOT / Trim（裁剪）发布**：WPF 被 .NET 8 SDK 硬性禁止（`NETSDK1168: WPF is not supported with trimming`）。当前发布方式为自包含单文件（`SelfContained` + `PublishSingleFile` + `ReadyToRun`），已是此架构下最优解。
- 版本号来源：UI 用 `SuperDucker.Shared.VersionHelper.GetVersion()`，须优先 `Assembly.GetEntryAssembly()` 以读宿主 App 版本（否则会错读 Shared 库默认 1.0.0）。

## 环境 / 工具链

- Git 推送 GitHub 需走代理：本地代理端口 **7890**。命令：
  `git config --global http.proxy http://127.0.0.1:7890` 和 `https.proxy` 同值。
  关闭代理软件后 git 会再报 443 超时，届时 `git config --global --unset http.proxy` 清除。
