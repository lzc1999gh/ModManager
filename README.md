<<<<<<< HEAD
﻿# ModManager

ModManager 是一个面向 GIMI（XXMI Launcher）的 Mod 管理器 WPF 桌面应用，用于按角色组织、启用/禁用、导入、预览游戏 Mod，并在 Mod 启停时自动保存与恢复 GIMI Persist 快捷键状态。

## 功能特性

- **按游戏与角色组织**：内置 GI（原神）与 WW（鸣潮）两个游戏预设，角色列表基于游戏头像资源自动生成
- **Mods 根目录管理**：为每个游戏独立设置 Mods 根目录，设置后持久化保存，重启应用无需重新配置
- **启禁用 Mod**：通过 `DISABLED_` 前缀重命名实现启停；切换时自动读取并保存当前 GIMI Persist 值，启用时自动恢复
- **一键激活**：同时关闭同角色其他已启用 Mod，并恢复目标 Mod 的 Persist 状态
- **拖放导入**：支持将文件夹、zip 压缩包或单个文件拖入角色列表完成导入（冲突时询问是否覆盖）
- **多预览图管理**：
  - 右键「添加预览」：将剪贴板图片保存为该 Mod 的预览图（`preview_x.png` 命名）
  - 右键「删除预览」：删除当前显示的预览图
  - 左右按键轮换显示多张预览图
- **Mod 详情**：双击重命名、记录来源链接、读取/打开 INI 文件、查看快捷键列表与 INI 内容

## 目录结构

```
ModManager/
├── Models/                  # 数据模型
│   ├── Game.cs              # 游戏（Id、Name、Path、ModsRootPath）
│   ├── Mod.cs               # Mod（名称、路径、预览图列表、来源、启停状态）
│   ├── Character.cs         # 角色（头像路径、Mod 列表）
│   └── IniShortcut.cs       # INI 快捷键项
├── ViewModels/              # 视图模型
│   ├── MainViewModel.cs     # 主逻辑（扫描、启停、导入、预览、Persist 联动、状态持久化）
│   └── RelayCommand.cs      # ICommand 实现
├── Services/
│   └── GimiPersistService.cs # GIMI Persist 状态管理（保存/恢复/迁移）
├── Views/                   # 视图（XAML + code-behind）
│   ├── CharacterView        # 角色网格
│   ├── ModListView          # 当前角色 Mod 列表
│   ├── ModDetailView        # Mod 详情（重命名、来源、INI）
│   └── ModPreviewView       # 预览图（右键添加/删除、左右轮换）
├── Converters/              # 值转换器
│   ├── ImagePathToImageSourceConverter.cs
│   └── IntGreaterThanZeroToVisibilityConverter.cs
├── Resources/               # 图标（Icons）、角色头像（CharacterPic）
├── App.xaml / MainWindow.xaml
└── ModManager.csproj
```

## 技术栈

- **.NET**：net10.0-windows（WPF）
- **架构**：MVVM（手写 `INotifyPropertyChanged` + `RelayCommand`）
- **NuGet 包**：
  - `SharpVectors` 1.8.5（SVG 图标渲染）
  - `Ookii.Dialogs.Wpf` 3.4.0（文件夹选择对话框）
- **序列化**：`System.Text.Json`（状态文件）

## 使用说明

1. 启动应用，在顶部工具栏选择游戏（GI / WW）
2. 点击「设置」选择该游戏的 Mods 根目录（根目录下每个子文件夹对应一个角色，角色文件夹内每个子目录/文件视为一个 Mod）
3. 在角色网格中选择角色，右侧列表展示该角色所有 Mod
4. 通过开关按钮或一键激活来启用/禁用 Mod（启用时自动恢复 GIMI Persist，禁用时自动保存）
5. 将 Mod 文件夹 / zip / 文件拖入列表即可导入
6. 在预览区：
   - 右键选择「添加预览」将剪贴板图片保存为预览图
   - 右键选择「删除预览」删除当前预览图
   - 点击左右箭头轮换查看多张预览图

## 数据存储

应用状态持久化于 `%LocalAppData%\ModManager\` 目录下：

| 文件 | 说明 |
|------|------|
| `modstate.json` | 游戏列表、Mods 根目录、角色与 Mod 的启用状态、来源等 |
| `gimi-persist.json` | 各 Mod 的 GIMI Persist 保存值（用于启停时恢复） |

> 注：`GimiPersistService` 通过读取/写入 `d3dx_user.ini`（路径在 `MainViewModel` 构造函数中配置）来保存与恢复 Persist 状态。
=======
这是一个99.9%由AI完成的YS和WW的MOD管理器，完全在chatgpt（白嫖）、deepseek两位老师的指导下实现
>>>>>>> 24766fb4ad327c3605cb3b25dfc4d3aa567d91fe
