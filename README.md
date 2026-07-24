---
AIGC:
  ContentProducer: '001191110102MAD55U9H0F10002'
  ContentPropagator: '001191110102MAD55U9H0F10002'
  Label: '1'
  ProduceID: 'b983e263-0c54-461f-bece-448a2ad1ccc5'
  PropagateID: 'b983e263-0c54-461f-bece-448a2ad1ccc5'
  ReservedCode1: 'fb8dee0f-0c27-4c57-ad8b-5df1e1fb1fb6'
  ReservedCode2: 'fb8dee0f-0c27-4c57-ad8b-5df1e1fb1fb6'
---

# 红警复刻 RTS

红警2灵感即时战略游戏，用 Godot 4.7.1 mono + C# (.NET 8) 开发。

## 特点

- 等距 2.5D 渲染（菱形瓦片地形 + 预渲染8方向精灵图）
- 8阵营对战（玩家 vs 7个AI），碎片时间15分钟一局
- 四档难度（新手/标准/困难/残酷）
- 文明6风格游戏性扩展：
  - G1 科技树（12节点3分支）
  - G2 时代系统（4时代进阶）
  - G3 战术卡（8种卡开局选择）
  - G4 电网分区（供电半径策略）
  - G5 尤里卡时刻（游戏事件触发免费科技）
  - G6 邻接加成（建筑布局策略）
  - G7 间谍深化（5种间谍任务）
  - G8 占领强化（连锁占领+叛变风险）
- 27种单位 / 12种建筑 / 9种地形
- 程序化地图生成（支持种子复现）

## 运行要求

- Godot 4.7.1 mono（.NET 8 SDK）
- Windows / Linux / macOS

## 构建

```
dotnet build
```

在 Godot 编辑器中打开项目，按 F5 运行，或通过 导出 > Windows Desktop 生成可执行文件。

## 操作

| 键 | 功能 |
|----|------|
| WASD/方向键 | 移动镜头 |
| 鼠标左键 | 选择/框选单位 |
| 鼠标右键 | 移动/攻击 |
| B/N/M/L/K/O/I | 生产单位/建筑 |
| Tab | 科技树面板 |
| Y/U | 时代面板/升级 |
| T | 战术卡面板 |
| G | 电网面板 |
| H | 尤里卡面板 |
| J | 邻接加成面板 |
| N | 间谍面板 |
| K | 占领面板 |
| Shift | 批量排产5x |
| F12 | 截图 |

## 技术栈

- 引擎: Godot 4.7.1 mono
- 语言: C# (.NET 8)
- 渲染: 等距2.5D（CPU渲染，无需GPU）
- 版本: v1.0.0

## 许可

MIT License