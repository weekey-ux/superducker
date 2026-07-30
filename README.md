# SuperDucker（超级潜水员）

> 绿色软件启动器 · 版本 **1.0.0**

SuperDucker（中文名「超级潜水员」）是一款 Windows 平台下的**便携式（绿色）软件快速启动器**。
它借鉴 macOS Spotlight / Alfred 的理念：通过简短的**缩写** + `Win+R`，瞬间启动你常用的程序或网址。

所有数据（数据库、图标、快捷方式、已安装软件）都存放在程序目录内，**不写入系统目录、不写注册表**，可整体拷贝到 U 盘或任意位置直接运行。

---

## 核心特性

- **缩写启动**：为每个程序设定简短缩写（如 `np` → Notepad++），`Win+R` 输入 `np` 回车即启动。
- **全局热键**：在任意界面按 `Ctrl+Space` 唤起搜索面板，输入缩写或关键字即时检索。
- **网址书签**：除本地程序外，还可登记常用网址，统一用缩写打开，并自动抓取站点 Favicon。
- **标签页分类**：将软件按用途分组（开发、办公、游戏…），面板以标签页形式切换。
- **绿色打包（.sdzip）**：把零散的绿色软件打包成一个自描述压缩包，可一键分享、一键导入。
- **便携式运行**：SuperDucker 及其管理的程序全部位于程序目录内，U 盘即插即用。
- **离线优先**：Favicon 仅从目标站点自身获取，不依赖任何第三方图标服务，完全离线可用。

---

## 项目结构

```
SuperDucker/
├── SuperDucker.App/        # WPF 图形界面（主程序 superducker.exe）
│   ├── MainWindow.xaml(.cs)       # 主搜索面板
│   ├── AddDialog.xaml(.cs)        # 添加/编辑 程序或网址
│   ├── PackDialog.xaml(.cs)       # 图形化打包工具
│   ├── SettingsDialog.xaml(.cs)   # 设置（热键、主题…）
│   ├── ShopPanel.xaml(.cs)        # 软件商店/导入面板
│   ├── CloseChoiceDialog.*        # 退出/最小化到托盘选择
│   ├── GlobalHotkeyManager.cs     # 全局热键注册
│   ├── SystemTrayManager.cs       # 系统托盘
│   └── DpiHelper.cs               # 高 DPI 图标缩放
│
├── SuperDucker.Shared/     # 共享库（App 与 CLI 共用）
│   ├── VersionHelper.cs           # 统一的程序集版本读取入口
│   ├── FileHelper.cs              # 文件辅助（如大小格式化）
│   ├── Data/
│   │   ├── DatabaseManager.cs      # SQLite 数据访问层
│   │   ├── WebHelper.cs            # Favicon 抓取（本地化策略）
│   │   ├── ShopManager.cs          # 软件商店清单管理
│   │   ├── ShortcutManager.cs      # 桌面/开始菜单快捷方式
│   │   ├── RescanHelper.cs         # 目录扫描恢复注册
│   │   └── IconHelper.cs           # 图标提取与缓存
│   ├── Models/                     # AppEntry / UrlEntry / PackItem / ShopItem…
│   ├── Helpers/                    # 缩写生成、快捷方式、导入恢复…
│   └── Native/                     # 资源管理器图标、ICO 提取、INI 等 Win32 封装
│
├── SuperDucker.Cli/        # 命令行工具（sd.exe）
│   └── Program.cs                  # 命令行入口，提供 add/list/pack/import 等子命令
│
└── publish/                # 发布输出目录（构建产物，不纳入源码）
```

---

## 快速开始

1. 运行 `superducker.exe` 打开主面板。
2. 点击「添加」登记你的第一个程序，并为其设定一个缩写（如 `calc`）。
3. 执行一次 `sd setup`（命令行）将 `link/` 目录注册进系统 `PATH`。
4. 此后在任何地方按 `Win+R`，输入缩写即可启动对应程序。

---

## 命令行（sd）

命令行工具 `sd.exe` 与面板共享同一数据库，常用命令：

| 命令 | 说明 |
|------|------|
| `sd <缩写>` | 启动程序 / 打开网址 |
| `sd s <缩写>` | 以管理员身份运行 |
| `sd d <缩写>` | 打开程序所在目录 |
| `sd add <缩写> <路径>` | 注册新程序 |
| `sd edit <缩写> ...` | 修改已注册条目 |
| `sd remove <缩写>` | 删除程序或网址 |
| `sd list` | 列出所有条目 |
| `sd url add <缩写> <网址>` | 注册网址书签 |
| `sd icon <缩写> --fetch` | 抓取网站图标（仅 URL） |
| `sd pack <目录> <包ID>` | 打包为 `.sdzip` 绿软包 |
| `sd pack-gui` | 启动图形化打包工具 |
| `sd import <包>.sdzip` | 导入绿软包 |
| `sd setup` | 注册 `link/` 到 PATH |
| `sd repair` | 修复快捷方式路径 |
| `sd rescan` | 扫描目录恢复丢失注册 |

运行 `sd help` 查看完整帮助。

---

## 构建与发布

需要 **.NET 8 SDK**（Windows）。

```powershell
# 发布图形界面（单文件、自包含）
dotnet publish SuperDucker.App/SuperDucker.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/app

# 发布命令行（单文件、自包含）
dotnet publish SuperDucker.Cli/SuperDucker.Cli.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/cli
```

> 发布前请将 `SuperDucker.App/SuperDucker.App.csproj` 与 `SuperDucker.Cli/SuperDucker.Cli.csproj` 中的 `<Version>` 递增到本次发行版本。

仓库根目录的 `builder.bat` 可一键完成「构建 + 单文件自包含发布到 `publish/`」：

```powershell
builder.bat                # 普通发布（默认，不混淆）
builder.bat -obfuscate     # 发布并启用 Obfuscar 混淆（先安装工具：dotnet tool install Obfuscar.GlobalTool -g）
```

### 可选的代码混淆

为降低逆向可读性的门槛（**注意：混淆不等于防反编译，仅提高逆向成本**），项目集成了 [Obfuscar](https://github.com/obfuscar/obfuscar)，通过 `Obfuscate` 开关控制，默认关闭：

```powershell
# 首次使用需安装混淆工具（全局）
dotnet tool install Obfuscar.GlobalTool -g

# 发布并启用混淆（单文件 / 非单文件均可，混淆在打包前作用于程序集）
dotnet publish SuperDucker.App/SuperDucker.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:Obfuscate=true -o publish/app
dotnet publish SuperDucker.Cli/SuperDucker.Cli.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:Obfuscate=true -o publish/cli
```

- 混淆在 `Publish` 前对 `$(TargetDir)` 下的程序集（含 `SuperDucker.Shared.dll`）重命名类型/方法/字段并隐藏字符串，随后被单文件捆绑或自包含发布一并打包，已实际验证 CLI 混淆后 `sd help` 正常运行。
- 混淆配置见各项目根目录的 `Obfuscar.xml`；WPF 的 XAML 绑定入口（`App`/`MainWindow`）、CLI 的 `Program`，以及被反射或公开调用的 `VersionHelper` / `FileHelper` 已加白名单豁免，避免运行时崩溃。
- 1.0.0 阶段建议保持默认（不混淆），便于开源审阅与问题排查；仅在确有分发保护需求时开启 `-p:Obfuscate=true`。

---

## 版本记录

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.0.0 | 2026-07-30 | 正式发行版：版本号切换为语义化版本（SemVer，主.次.修订）；标题栏与设置页统一显示版本号；抽取 `FormatSize` 到 `FileHelper` 共享以消除重复；删除死代码 `GetVersionString` 并修正 `PackDialog` 注释瑕疵；清理根目录冗余文件；补充并规范核心代码中文注释；集成可开关的 Obfuscar 混淆（`builder.bat -obfuscate`）。 |

---

## 许可证

本项目以 **MIT License** 开源，是最宽松的许可证之一：你可以自由使用、复制、修改、分发（含闭源/商业用途），只需保留原始版权声明与许可声明即可。

- 许可证全文见仓库根目录 [`LICENSE`](./LICENSE)。
- Copyright © 2026 SuperDucker（超级潜水员）。

> 软件按「原样」提供，不附带任何明示或暗示的担保。在使用过程中产生的任何后果，作者与贡献者不承担法律责任。
