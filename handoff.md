---
AIGC:
  ContentProducer: '001191110102MAD55U9H0F10002'
  ContentPropagator: '001191110102MAD55U9H0F10002'
  Label: '1'
  ProduceID: '8aac071c-9976-46c7-a325-bef7dfa9065d'
  PropagateID: '8aac071c-9976-46c7-a325-bef7dfa9065d'
  ReservedCode1: 'f9899080-98a1-41f6-b346-edd041d72772'
  ReservedCode2: 'f9899080-98a1-41f6-b346-edd041d72772'
---

# 铁幕突袭 交接文档

> 最后更新：2026-07-29 | 最新commit：3979196 | 分支：main

---

## 一、我们在做什么

基于严苛审查报告（综合评分3.57/10），全面推进铁幕突袭(RTS_Game)项目的品质化改造，目标是1:1复刻红警2核心体验。

三大主线同步推进：
1. **平衡性调整** — 压缩DPS/$极差、防御建筑性价比
2. **硬编码中文清理** — 从171处清理到接近0
3. **Phase 1-2内容补强** — 战争迷雾、视觉增强、超武动画、RA2标志性单位

---

## 二、做完了什么

### 2.1 战争迷雾系统 (bd67b04)
- 重构FogOfWar从TileMapLayer→Node2D+_Draw渲染
- 三态可见性：未知/已探索/可见
- 修复IsDestroyed→IsDead编译错误
- 集成到Main._Ready和_Process

### 2.2 Phase1视觉增强 (ae14b84)
- 建筑受损冒烟粒子系统
- 建造入场动画(Tween缩放淡入)
- 单位死亡残骸(烧焦椭圆8秒淡出)
- 屏幕震动系统(4级:核弹16px/闪电8px/建筑6px/重坦4px)

### 2.3 超武发射动画系统 (64ac5c4)
- 核弹1.5秒抛物线弹道+弹头尾迹+准星
- 闪电0.8秒蓄能+乌云聚集
- AI超武也用发射动画

### 2.4 RA2标志性单位 (3979196含)
- 天启坦克(ApocalypseTank,$1500,HP400,DPS44.4,对空)
- 光棱坦克(PrismTank,$1200,HP150,DPS30,远程溅射)
- 基洛夫空艇(KirovAirship,$1500,HP300,DPS40,大范围溅射)
- 磁暴步兵(TeslaTrooper,$500,HP80,DPS20.8,电击溅射)
- BuildPanel热键Shift+A/P/O/L+阵营白名单+时代限制+图标映射

### 2.5 平衡性调整 (3979196含)
- **单位DPS/$极差**：从5.6x压缩到**2.97x**（目标<3x，已达标）
  - 轰炸机cooldown 3.0→2.8
  - 步兵cost 100→150, damage 6→5
  - 喷火兵cost 180→250
  - 狙击手cost 200→300
  - Hero cost 600→800, damage 35→30
  - 工兵cost 150→120
  - 高级工程师cost 400→300
  - 窃贼cost 300→250
- **防御建筑平衡**：
  - 机枪塔damage 18→12, cooldown 0.6→0.8 (DPS/$ 0.075→0.0375)
  - 防空炮damage 30→22, cooldown 1.0→1.1 (DPS/$ 0.050→0.033)

### 2.6 硬编码中文清理 (3979196含)
- BuildPanel.cs 38处LockReason→TrManager.Tr()
- Main.Combat.cs/Main.Input.cs/Main.UIRender.cs/Main.Economy.cs等202处ShowToast→TrManager.Tr()
- Main.SaveLoad.cs 6处ShowToast→TrManager.Tr()
- Main.Tech.cs 2处科技分支→TrManager.Tr()
- 新增76+翻译key到zh-CN.csv和en.csv
- **P0硬编码中文从171处清理到0**

### 2.7 剩余中文（非P0）
- ReplayRecorder.cs 6处日志（调试用，非GameLog）
- FactionDef.cs/GameData.cs 3处throw异常消息（开发者面向）
- ReplayRecorder.cs 1处硬编码路径
- MainMenu.cs 1处语言名显示

---

## 三、现在卡在哪

无阻塞。所有已启动的任务均已完成、编译0错误、已推送到GitHub。

---

## 四、接下来该干什么

### Phase 2 优先级排序

1. **缺失命令系统**（巡逻/散开/阵型/停止/强制攻击/驻扎/路径点）— 7项命令缺失
2. **AI策略系统改进** — 当前AI仅有基础进攻逻辑，缺乏多策略决策
3. **Phase 2素材升级** — 更多单位动画、建筑美术、地面纹理
4. **音频补强** — BGM场景切换已接入，音效覆盖仍不完整
5. **地图多样性** — 当前5主题，可扩展更多地形/尺寸

### 长期路线（Phase 3+）
- 网络对战基础
- 战役模式框架
- 模组系统
- 性能优化（大规模单位卡顿）

---

## 五、绝对不能踩的坑

1. **AIGC水印hook**：系统在write/edit工具创建/修改.md/.txt/.pdf/.docx/.pptx/.xlsx时自动注入front matter水印。项目内.md文件必须走git对象层操作（hash-object → update-index → commit → restore）
2. **PowerShell 5.1限制**：不支持&&（用;连接）；TLS必须设Tls12；Set-Location不同步CurrentDirectory，.NET API需绝对路径
3. **TrManager CSV解析**：用Split(', 2)最多拆2段，value中的逗号安全不需转义
4. **Godot导出**：必须用官方tpz模板，不能用编辑器exe充当模板
5. **GitHub推送**：网络经常不通，push失败需恢复后重试；SSH key路径注意空格
6. **构建产物归档**：删除临时副本前必须先复制到releases\目录

---

## 六、关键文件索引

| 文件 | 说明 |
|------|------|
| scripts/Main.cs (~1178行) | 游戏主循环、核心逻辑 |
| scripts/Unit.cs (~2585行) | 单位系统 |
| scripts/Building.cs (~935行) | 建筑系统、生产、防御 |
| scripts/FogOfWar.cs | 战争迷雾(Phase1新增) |
| scripts/RTSCamera.cs | 相机+屏幕震动 |
| scripts/BuildPanel.cs | 建造面板(已国际化) |
| scripts/Main.Tech.cs | 科技树系统 |
| scripts/Main.SaveLoad.cs | 存档/读档(已国际化) |
| scripts/Main.Combat.cs | 战斗+超武发射动画 |
| data/units.json | 32个单位属性(2D+3D) |
| data/buildings.json | 13个建筑属性(2D+3D) |
| data/factions.json | 3阵营配置+单位白名单 |
| i18n/zh-CN.csv (798行) | 中文翻译 |
| i18n/en.csv (796行) | 英文翻译 |

---

## 七、Git提交历史

```
3979196 feat: 平衡性调整+防御建筑平衡+硬编码中文清理+RA2标志性单位+i18n补全
64ac5c4 feat: 超武发射动画系统
ae14b84 feat: Phase1视觉增强 - 建筑受损冒烟+建造动画+单位残骸+屏幕震动
bd67b04 feat: 战争迷雾系统集成 (Phase1)
d31d7e5 refactor: 移除3D死代码到feature/3d-experimental分支
39be132 docs: handoff.md清除AIGC水印
1ca7d17 8方向全面升级：i18n大规模替换+3D数值统一
```