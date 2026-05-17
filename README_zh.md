# MultiplayerRestart - 杀戮尖塔2 多人联机重开 Mod

[English](README.md)

在多人联机游戏中快速重开一局，无需手动返回主菜单。

## 功能

- **快捷键重开**: 按 `Ctrl+Shift+R` 触发重开
- **暂停菜单按钮**: 自动在暂停菜单中注入"重开一局"按钮
- **权限控制**: 只有房主可以发起重开
- **确认弹窗**: 重开前显示确认对话框，防止误操作
- **多策略执行**: 自动选择最佳方式执行重开（游戏API -> 返回主菜单 -> 强制断开）

## 兼容性

- **游戏版本**: v0.103.2
- **依赖**: 无（不需要 BaseLib）
- **多人要求**: 所有联机玩家都需要安装此 mod

## 环境要求

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- 杀戮尖塔2 已安装

## 构建步骤

### 1. 配置游戏路径

mod 会自动从 Steam 注册表检测游戏安装路径。如果检测失败，编辑 `Directory.Build.props`：

```xml
<Sts2Path>C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2</Sts2Path>
```

或者手动复制以下 DLL 到 `references/` 目录：
- `sts2.dll`
- `0Harmony.dll`
- `GodotSharp.dll`

这些文件位于游戏目录下的 `data_sts2_windows_x86_64/` 文件夹中。

### 2. 编译

```bash
dotnet build -c Release
```

### 3. 安装

编译后文件会自动复制到游戏的 `mods/MultiplayerRestart/` 目录。

如果自动复制失败，手动复制以下文件到 `<游戏目录>/mods/MultiplayerRestart/`：
- `bin/Release/MultiplayerRestart.dll`
- `MultiplayerRestart.json`

### 4. 启用 Mod

1. 启动游戏
2. 首次会弹出 mod 启用确认
3. 确认后重启游戏

## 使用方法

### 快捷键
在游戏中按 `Ctrl+Shift+R` 即可触发重开。

### 暂停菜单
如果 mod 成功检测到暂停菜单，会自动添加 "Restart Run" 按钮。

### 重开流程
1. 房主按下快捷键或点击按钮
2. 弹出确认对话框
3. 确认后，当前 run 被放弃
4. 所有玩家返回主菜单
5. 房主可立即创建新的多人大厅

## 多人游戏测试

使用启动参数进行本地多人测试：

**主机端:**
```
--force-steam=off --fastmp host --clientId 1
```

**客户端:**
```
--force-steam=off --fastmp join --clientId 1000
```

## 故障排除

### 暂停菜单按钮未出现
这是正常的。mod 使用运行时发现来查找暂停菜单类，如果游戏版本的类名不在候选列表中，按钮将不会出现。此时请使用 `Ctrl+Shift+R` 快捷键。

### 重开后未自动返回主菜单
mod 通过反射查找游戏的放弃/返回方法。如果找不到，会使用强制断开连接的方式。查看游戏日志中的 `[MultiplayerRestart]` 前缀信息获取详情。

### 自定义游戏 API 路径
如果你反编译了 `sts2.dll` 并找到了正确的类名/方法名，可以在 `src/Utils/GameApi.cs` 中更新候选列表。

## 项目结构

```
src/
├── ModEntry.cs              # Mod 入口
├── RestartManager.cs        # 重开流程管理
├── InputHandler.cs          # 键盘快捷键
├── Patches/
│   ├── PauseMenuPatch.cs    # 暂停菜单注入
│   └── RunLifecyclePatch.cs # Run 生命周期跟踪
└── Utils/
    └── GameApi.cs           # 游戏 API 反射发现
MultiplayerRestart.csproj    # 项目配置
MultiplayerRestart.json      # Mod 清单
Directory.Build.props        # 本地构建配置
Sts2PathDiscovery.props      # STS2 路径自动检测
```

## 许可证

MIT License
