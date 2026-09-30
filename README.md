# ModManager

[![Build](https://github.com/lzc1999gh/ModManager/actions/workflows/build.yml/badge.svg)](https://github.com/lzc1999gh/ModManager/actions/workflows/build.yml)

ModManager 是一个基于 WPF 的 Windows Mod 管理器，面向使用 3DMigoto、XXMI、GIMI 等配置方式的游戏 Mod。

项目当前预置 GI 和 WW 两个游戏配置，也支持添加其他游戏。应用数据统一保存在程序目录下，便于复制、迁移和删除整个项目。

## 功能概览

- 管理多个游戏及其 Mods 根目录、游戏图标和 `d3dx_user.ini` 路径。
- 按角色组织文件夹 Mod 和单文件 Mod，支持启用、禁用、导入、重命名、删除和打开 Mod 目录。
- 角色列表来源于 `CharacterInfo.json`，不依赖角色头像是否存在。
- 角色可以没有头像；可通过右键菜单添加或修改头像，头像会复制到当前游戏的 `CharacterPic` 目录并按角色名保存。
- 支持新增角色、修改角色名，并同步迁移角色目录、头像和管理器状态。
- 支持隐藏和取消隐藏角色；隐藏只影响列表显示，不删除角色信息、不移动 Mod 目录，也不影响 persist 状态。
- 角色列表右上角有三个圆点按钮，切换显示范围：默认列表（不含隐藏）/ 仅有 Mod 的角色 / 全部列表（含隐藏）。
- 支持多张 Mod 预览图，并提供添加、删除、上一张和下一张操作。
- 递归读取 Mod 中包含 `Key...` 节和 `key=` 配置项的 INI 文件，并为每个 INI 文件提供独立按钮。
- 读取和显示快捷键，不编辑或保存快捷键配置。
- 按游戏和 Mod 保存、恢复 `global persist` 状态，删除 Mod 时同步删除对应快照。
- GI 和 WW 支持从官方角色图鉴同步角色名称和头像。

## 系统要求

运行主程序需要：

- Windows 10 或更高版本；
- .NET 10 Desktop Runtime，或直接使用 GitHub Release 提供的自包含版。

使用角色同步功能还需要：

- Python 3；
- Chrome 或 Edge；
- 可访问对应官方图鉴页面的网络连接。

角色同步使用 Python 标准库和本机 Chrome/Edge 的无头模式，不需要额外安装 Python 第三方包。也可以通过 `MODMANAGER_PYTHON` 和 `MODMANAGER_BROWSER` 环境变量指定 Python 或浏览器路径。

## 下载与运行

推荐从 [GitHub Releases](https://github.com/lzc1999gh/ModManager/releases) 下载 Windows 发布包。

- `*-win-x64.zip`：自包含版，解压后即可运行，不需要安装 .NET。
- `*-portable.zip`：框架依赖版，体积较小，需要安装 .NET 10 Desktop Runtime。
- `*-win-x64-full.zip` 和 `*-portable-full.zip`：包含默认 `Data` 数据，适合全新安装。

解压后运行 `ModManager.exe`。发布目录中的文件和目录需要保持原有结构，尤其不能移除 `Resources`、`Tools` 或 `Data` 目录。

## 从源码构建

```powershell
git clone https://github.com/lzc1999gh/ModManager.git
cd ModManager
dotnet restore .\ModManager\ModManager.csproj
dotnet build .\ModManager\ModManager.csproj -c Release --no-restore
```

在 Visual Studio 中也可以打开 `ModManager.slnx`，选择 `ModManager` 项目运行。

生成自包含 Windows x64 发布目录：

```powershell
dotnet publish .\ModManager\ModManager.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\publish\ModManager
```

## 快速使用

### 配置游戏

从左侧游戏栏选择已有游戏，或选择末尾的“增加游戏”图标。新增或修改游戏时可以设置：

| 配置项 | 说明 |
| --- | --- |
| 游戏 ID | 用于区分游戏和保存游戏状态，建议使用稳定且唯一的短名称。 |
| 游戏名称 | 在界面中显示的名称。 |
| Mods 根目录 | 该游戏的 Mods 根目录。目录不存在或为空时，Mod 列表、导入和启用/禁用功能不可用。 |
| 游戏图标 | 可选，支持 SVG、PNG、JPG、JPEG、BMP、GIF。 |
| `d3dx_user.ini` | 用于读取当前生效 Mod 的 persist 状态，可留空；只有相关 Mod 声明了 `global persist` 且无法定位该文件时才需要处理。 |

游戏图标右键菜单提供修改和删除功能。删除游戏只删除管理器保存的游戏数据，不会删除 Mods 根目录中的文件。

### Mods 目录结构

角色目录必须与 `CharacterInfo.json` 中的角色名一致，Mod 直接放在角色目录下：

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

名称以 `DISABLED_` 开头的文件或文件夹会显示为禁用状态。切换 Mod 状态时，程序通过增加或移除该前缀完成启用和禁用。

导入支持以下压缩格式：

```text
.zip  .7z  .rar  .tar  .gz  .bz2  .xz
```

加密压缩包和分卷压缩包可能无法直接导入。

### 角色和头像

角色信息保存在当前游戏的 `CharacterInfo.json` 中。新增角色时只创建角色信息，不要求同时设置头像。

头像可以为空。右键角色图像区域，选择“修改头像”后选择图片，程序会将图片复制到当前游戏的 `CharacterPic` 目录，并使用角色名作为文件名。修改角色名时，程序会同步迁移角色头像和角色目录。

同一个右键菜单还提供“隐藏角色 / 取消隐藏”。隐藏只作用于列表显示：

- 不删除角色信息，不移动或删除该角色的 Mod 目录，也不影响 persist 状态；
- 隐藏状态随 `state.json` 保存，下次启动仍然生效；
- 隐藏后的角色不出现在默认列表和有 Mod 列表中，也不会因为切换列表模式而重新出现（“全部列表”除外）。

角色标题栏右侧有三个圆点按钮，用于切换列表的显示范围：

| 圆点 | 列表 | 内容 |
| --- | --- | --- |
| 灰色 | 默认列表 | 全部角色，不含被隐藏的。 |
| 红色 | 仅有 Mod 的角色 | 只显示已扫描到 Mod 的角色，同样不含被隐藏的；未配置 Mods 根目录时该列表为空。 |
| 蓝色 | 全部列表 | 包含被隐藏的角色，隐藏项以半透明淡化显示，可在此通过右键菜单取消隐藏。 |

### Mod 预览

为了避免把角色贴图或 Mod 内的其他图片误识别为预览图，程序只读取顶层且符合命名约定的图片：

- 文件夹 Mod：`preview_*.png`、`preview_*.jpg`、`preview_*.jpeg`；
- 单文件 Mod：`原文件名.preview_*.png`、`原文件名.preview_*.jpg`、`原文件名.preview_*.jpeg`。

一个 Mod 可以有多张预览图，程序会按文件名排序并支持切换查看。

### INI 快捷键

选中 Mod 后，程序会递归扫描其中的 `.ini` 文件。满足以下条件的 INI 文件会显示为独立按钮：

```ini
[KeyToggle]
key = VK_F1
```

所有以 `Key` 开头的节中，名称为 `key` 的配置项都会被读取。点击不同的 INI 按钮时，下方只显示当前选中 INI 文件中的快捷键内容；“打开”按钮打开当前选中的 INI 文件。

当前快捷键功能仅用于读取和显示，不会编辑快捷键，也不会把 `key=` 写入 `d3dx_user.ini`。

### 同步角色信息

选择 GI 或 WW 后，点击“角色”标题栏中的“同步”按钮：

- GI：[原神观测枢角色图鉴](https://baike.mihoyo.com/ys/obc/channel/map/189/25?bbs_presentation_style=no_header&visit_device=pc)；
- WW：[鸣潮库街区角色图鉴](https://wiki.kurobbs.com/mc/catalogue/list?fid=1099&sid=1105)。

同步是增量操作：

- 只新增本地缺少的角色；
- 下载缺少的头像，不覆盖已有头像；
- 不删除本地角色、自定义角色、Mod 目录或已有状态；
- 官方页面访问失败时，不修改本地角色信息文件。

官方图鉴会把主角按元素或性别拆成多条（原神 7 条「旅行者·冰 / 火 / 水 / 草 / 雷 / 岩 / 风」，鸣潮 8 条「漂泊者-男 / 女-…」）。同步时会先把这些条目归一为「旅行者」和「漂泊者」再去重，因此每次同步最多写入一条主角，头像取图鉴中第一个可用的。

原神图鉴页面中的说明性链接“如何成为观测者”不会被加入角色列表。

### Persist 状态

部分 Mod 会声明 `global persist` 变量：

```ini
[Constants]
global persist $example = 0
```

同一角色运行时通常只保留当前生效 Mod 的 persist 信息。因此程序在切换 Mod 时：

1. 禁用当前 Mod 前，从 `d3dx_user.ini` 读取当前值；
2. 将该 Mod 的值保存到当前游戏的 persist 快照文件；
3. 启用目标 Mod 前，将目标 Mod 的历史值写回其 INI 文件；
4. 等待游戏运行环境重新加载并生成当前 Mod 的用户状态。

程序不会把多个 Mod 的 persist 信息同时写入 `d3dx_user.ini`。删除 Mod 时会删除该 Mod 的快照；重命名 Mod、角色或游戏时会迁移对应快照。

## 应用数据

所有应用数据都保存在程序目录下的 `Data` 文件夹中：

```text
Data/
└─ Games/
   └─ <游戏ID>/
      ├─ game.json
      ├─ CharacterInfo.json
      ├─ CharacterPic/
      ├─ GameIcon.svg
      ├─ state.json
      └─ Persist/
         └─ snapshots.json
```

| 文件或目录 | 内容 |
| --- | --- |
| `game.json` | 游戏名称、Mods 根目录、游戏图标和 `d3dx_user.ini` 路径。 |
| `CharacterInfo.json` | 当前游戏的角色列表；内置角色与用户新增角色统一保存在这里。 |
| `CharacterPic/` | 当前游戏的角色头像，允许为空。 |
| `GameIcon.*` | 当前游戏的图标。 |
| `state.json` | 角色（含隐藏状态）、Mod 来源、预览图等管理器状态。 |
| `Persist/snapshots.json` | 当前游戏独立保存的 `global persist` 历史快照。 |

GI 和 WW 的内置信息也只是预先放入 `Data/Games` 的用户数据，程序不会从 `Resources/CharacterInfo` 或 `Resources/CharacterPic` 读取角色信息。

升级时请使用不带 `-full` 的发布包，以保留已有 `Data`；首次安装或需要恢复默认数据时使用带 `-full` 的发布包。

## 项目结构

```text
ModManager/
├─ ModManager.slnx
├─ ModManager/
│  ├─ Models/          数据模型
│  ├─ Services/        业务服务与数据访问
│  ├─ ViewModels/      界面逻辑和命令
│  ├─ Views/           WPF 用户控件和对话框
│  ├─ Styles/          设计令牌与共享控件样式
│  ├─ Converters/      值转换器
│  ├─ Resources/       程序图标和界面资源
│  ├─ Tools/           角色图鉴同步脚本
│  └─ ModManager.csproj
├─ Data/Games/         内置及用户游戏数据
├─ .github/workflows/  CI 和 Release 工作流
└─ README.md
```

界面逻辑按职责分层。`ViewModels/MainViewModel.cs` 只保留状态、命令与流程编排，具体业务下沉到 `Services/`：

| 服务 | 职责 |
| --- | --- |
| `GameService` | 游戏的增删改、图标与目录迁移。 |
| `CharacterService` | 角色信息读写、头像与重命名。 |
| `ModService` | Mod 扫描、导入、删除与重命名。 |
| `PreviewService` | 预览图查找、增删与翻页。 |
| `IniService` | INI 扫描与快捷键解析。 |
| `StateStore` / `GimiPersistService` | 管理器状态与 `global persist` 快照的持久化。 |
| `CharacterInfoSyncService` | 官方角色图鉴同步；主角名称归一规则见 `ProtagonistAliases`。 |
| `DialogService` | 弹窗统一入口（`IDialogService`）。 |

所有弹窗都通过 `IDialogService` 走项目自绘的 `MessageDialog`，与主界面共用同一套设计语言，不依赖系统原生 `MessageBox`。

界面外观集中在 `Styles/`：`Theme.xaml` 定义颜色、字号、圆角、间距等设计令牌，`Controls.xaml` 定义按钮、开关、输入框、右键菜单等共享控件模板。修改外观时优先改这两处，不要在视图中内联样式。

图片统一通过 `Converters/ImagePathToImageSourceConverter` 载入，它内置解码缓存（按文件写入时间与长度失效，改动图片后会自动重新读取）。列表中的小图应传入 `ConverterParameter=<解码宽度>` 走缩略图解码，避免反复解码整图造成卡顿。

## 已知限制

- 目前仅支持 Windows/WPF；GitHub Release 提供的自包含包目标为 `win-x64`。
- 快捷键目前只能读取和显示，不能在管理器内编辑或保存。
- 角色目录名、Mod 名称和角色名应避免使用 Windows 文件名非法字符。
- 程序不会替用户下载 Mod，也不会自动修改游戏本体文件。
- 同步功能依赖官方页面结构，页面改版后可能需要更新爬虫。
- 主角归一只作用于同步写入的数据。如果本地角色表中已经存在历史遗留的多个主角条目（例如 7 条「旅行者·X」），同步不会自动清理它们，需要手动删除这些条目后重新同步。
- 仓库当前未附带许可证文件。除非获得项目作者许可，否则不应将本项目代码作为已授权开源软件再分发。

## 开发与贡献

欢迎提交 Issue 或 Pull Request。提交修改前建议运行：

```powershell
dotnet build .\ModManager\ModManager.csproj -c Release
git diff --check
```

CI 会在推送到 `master` 分支或创建针对 `master` 的 Pull Request 时构建项目。推送符合 `v<major>.<minor>.<patch>` 格式的 Git 标签时，Release 工作流会生成并发布四类 Windows ZIP 包。

## 许可证

当前仓库未提供许可证文件。使用、修改或再分发前，请先获得项目作者授权。
