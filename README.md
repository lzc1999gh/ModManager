# ModManager

[![Build](https://github.com/lzc1999gh/ModManager/actions/workflows/build.yml/badge.svg)](https://github.com/lzc1999gh/ModManager/actions/workflows/build.yml)

一个基于 WPF 的 Windows Mod 管理器，主要用于管理支持 3DMigoto、XXMI 或 GIMI 配置方式的游戏 Mod。项目当前内置 GI 和 WW 示例游戏配置，也支持用户自行增加其他游戏。

## 功能特性

- 支持多个游戏，每个游戏可以单独配置：
  - Mods 根目录；
  - 角色头像目录；
  - 角色信息 JSON 文件；
  - `d3dx_user.ini` 文件路径。
- 角色列表来自角色信息文件，不依赖头像文件是否存在。
- 角色可以没有头像；可以通过角色图像区域的右键菜单添加或修改头像，头像会复制到对应的 `CharacterPic` 目录并按角色名保存。
- 支持新增角色、修改角色名，并同步迁移角色目录、头像和相关状态。
- 支持管理文件夹 Mod、单文件 Mod 以及 ZIP 导入。
- 支持启用和禁用 Mod。禁用时使用 `DISABLED_` 前缀重命名文件或文件夹。
- 支持修改 Mod 名称、填写来源、删除 Mod 和打开 Mod 所在目录。
- 支持 Mod 预览图片的添加、删除、上一张和下一张。
- 递归读取 Mod 中的 INI 文件；包含 `Key...` 节并定义 `key=` 的 INI 文件会单独显示为按钮。
- 支持在不同 INI 文件之间切换，并查看当前 INI 中读取到的快捷键；也可以切换为查看完整 INI 内容。
- 读取和恢复 `global persist` 值，避免切换 Mod 后丢失 Mod 的运行时持久化状态。

## 运行环境

- Windows 10 或更高版本；
- .NET 10 SDK（构建项目需要）；
- WPF 运行环境由 Windows 提供。

本项目不是跨平台应用，不能在 Linux 或 macOS 上运行 WPF 主程序。

## 获取和构建

```powershell
git clone https://github.com/lzc1999gh/ModManager.git
cd ModManager
dotnet restore .\ModManager\ModManager.csproj
dotnet build .\ModManager\ModManager.csproj -c Release --no-restore
```

也可以直接从 GitHub 的 [Releases](https://github.com/lzc1999gh/ModManager/releases) 下载 Windows 发布包。

若需要生成可直接复制使用的自包含 Windows x64 版本：

```powershell
dotnet publish .\ModManager\ModManager.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\publish\ModManager
```

发布目录中的文件需要保持原有目录结构，尤其是 `Resources` 目录不能单独移除。

## 使用方法

### 1. 配置游戏

从顶部游戏下拉菜单中选择已有游戏，或选择最后的“增加游戏”选项。新增游戏时填写：

| 配置项 | 说明 |
| --- | --- |
| 游戏 ID | 用于区分游戏和保存游戏状态，建议使用稳定且唯一的短名称。 |
| 游戏名称 | 在界面中显示的名称。 |
| Mod 根目录 | 该游戏的 Mods 根目录。角色目录应直接位于此目录下。 |
| 角色头像目录 | 可选。头像文件放置目录；留空时使用应用数据目录。 |
| `d3dx_user.ini` | 可选。用于读取当前生效 Mod 的 persist 状态；留空时会尝试根据 Mods 根目录推断。 |

下拉菜单中的游戏可以修改或删除。删除游戏只删除管理器保存的游戏配置、角色信息和 persist 快照，不会删除磁盘上的 Mods 文件。

### 2. 目录结构

推荐的 Mods 目录结构如下：

```text
Mods/
├─ 角色名 A/
│  ├─ Mod 目录/
│  │  ├─ *.ini
│  │  ├─ *.buf
│  │  └─ preview_1.png
│  └─ 单文件 Mod.ini
└─ 角色名 B/
   └─ DISABLED_Mod 目录/
```

角色目录名称需要与角色信息文件中的角色名一致。管理器会扫描角色目录下的文件夹和文件，并将名称以 `DISABLED_` 开头的项目显示为禁用状态。

ZIP 导入时，管理器会优先解压为同名 Mod 目录；解压失败时会作为单文件 Mod 复制。

### 3. 预览图片

为了避免把角色贴图或 Mod 内的其他图片误识别为预览图，管理器只读取符合命名约定的图片：

- 文件夹 Mod：`preview_*.png`、`preview_*.jpg`、`preview_*.jpeg`；
- 单文件 Mod：`原文件名.preview_*.png`、`原文件名.preview_*.jpg`、`原文件名.preview_*.jpeg`。

预览图片只在 Mod 所在目录的顶层查找，不会递归扫描子目录。

### 4. INI 快捷键读取

选中 Mod 后，管理器会递归扫描该 Mod 中的 `.ini` 文件。满足以下条件的文件会生成一个 INI 按钮：

```ini
[KeyToggle]
key = VK_F1
```

所有以 `Key` 开头的节中，名称为 `key` 的配置项都会作为快捷键读取并显示。多个 INI 文件会分别生成按钮，点击按钮即可切换当前显示的快捷键内容；“打开”按钮会打开当前选中的 INI 文件。

当前快捷键功能只负责读取和显示，不会编辑或保存快捷键值，也不会把 `key=` 写回 `d3dx_user.ini`。

### 5. Persist 状态

部分 Mod 会在 INI 中声明 `global persist` 变量，例如：

```ini
[Constants]
global persist $example = 0
```

由于运行时同一角色通常只会保留当前生效 Mod 的 persist 信息，管理器在禁用当前 Mod 前读取配置，在启用目标 Mod 前恢复该 Mod 的历史值到对应 INI 声明中。`d3dx_user.ini` 仍由游戏运行环境负责生成和维护，管理器不会把多个 Mod 的状态同时写入其中。

删除 Mod 时会同时删除该 Mod 保存的 persist 快照；重命名 Mod、角色或游戏时会迁移对应状态。

## 应用数据文件

管理器会在 `%LocalAppData%\ModManager` 下保存用户状态：

| 文件或目录 | 内容 |
| --- | --- |
| `mod_manager_state.json` | 游戏配置、角色状态、Mod 来源等管理器界面状态。 |
| `mod_persist_snapshots.json` | 按游戏和 Mod 保存的 `global persist` 历史快照。 |
| `CharacterInfo\<游戏ID>.json` | 用户可修改的角色信息文件。首次修改内置角色列表时会从内置文件复制到这里。 |
| `CharacterPic\<游戏ID>\` | 未指定头像目录时使用的用户头像目录。 |

旧版本的 `modstate.json` 和 `gimi-persist.json` 会在启动时自动迁移到新的文件名。

## 项目结构

```text
ModManager/
├─ ModManager.slnx
├─ ModManager/
│  ├─ Models/          数据模型
│  ├─ Services/        persist 状态等服务
│  ├─ ViewModels/      界面逻辑和命令
│  ├─ Views/           WPF 用户控件和对话框
│  ├─ Resources/       图标、角色信息和内置头像
│  └─ ModManager.csproj
├─ .github/workflows/  GitHub Actions 构建配置
└─ README.md
```

## 已知限制

- 目前只支持 Windows/WPF 和 `win-x64` 自包含发布流程。
- 快捷键目前仅支持读取和显示，不能在管理器内编辑或保存。
- Mod 目录名称和角色目录名称应避免使用 Windows 文件名非法字符。
- 管理器不会替用户下载 Mod，也不会自动修改游戏本体文件。
- 仓库当前未附带许可证文件。除非另行获得项目作者许可，否则不应将本项目代码作为已授权开源软件进行再分发。

## 贡献

欢迎提交 Issue 或 Pull Request。提交修改前建议先确认：

```powershell
dotnet build .\ModManager\ModManager.csproj -c Release
git diff --check
```

## 版本与发布

版本发布使用 Git 标签标记，例如 `v0.1.0`。Windows 发布包由 `.github/workflows/build.yml` 中的 GitHub Actions 在 `master` 分支构建，目标为自包含 `win-x64` 应用。

