---
AIGC:
  ContentProducer: '001191110102MAD55U9H0F10002'
  ContentPropagator: '001191110102MAD55U9H0F10002'
  Label: '1'
  ProduceID: '15dfd68d-3587-4679-bed2-656744a8d3cb'
  PropagateID: '15dfd68d-3587-4679-bed2-656744a8d3cb'
  ReservedCode1: 'd6f5ad6c-dbce-41ac-9b5f-0296d21a2de4'
  ReservedCode2: 'd6f5ad6c-dbce-41ac-9b5f-0296d21a2de4'
---

# 铁幕突袭 交接文档

> 最后更新：2026-07-29 | 最新commit：d4fcd57 | 分支：main

---

## 一、我们在做什么

基于严苛审查报告（综合评分3.57/10），全面推进铁幕突袭(RTS_Game)项目的品质化改造，目标是1:1复刻红警2核心体验。

三大主线同步推进：
1. **平衡性调整** — DPS/$极差压缩、防御建筑性价比
2. **硬编码中文清理** — P0级从171处清零
3. **Phase 1-2内容补强** — 战争迷雾/视觉增强/超武动画/RA2单位/命令系统/AI策略/音频

---

## 二、做完了什么

### 2.1 战争迷雾系统 (bd67b04)
- 重构FogOfWar: TileMapLayer→Node2D+_Draw渲染，三态可见性

### 2.2 Phase1视觉增强 (ae14b84)
- 建筑受损冒烟粒子 + 建造入场动画 + 单位死亡残骸 + 4级屏幕震动

### 2.3 超武发射动画系统 (64ac5c4)
- 核弹抛物线弹道+闪电蓄能动画+AI超武动画

### 2.4 RA2标志性单位 (3979196)
- 天启坦克/光棱坦克/基洛夫空艇/磁暴步兵 + BuildPanel热键+阵营白名单

### 2.5 平衡性调整 (3979196)
- 单位DPS/$极差：5.6x→**2.97x**（<3x达标）
- 防御建筑：机枪塔DPS/$ 0.075→0.0375，防空炮0.050→0.033

### 2.6 硬编码中文P0清零 (3979196)
- 171→0处，202+8处替换为TrManager.Tr()，85+翻译key

### 2.7 7项命令系统 (eae6d16)
- **强制攻击(A键)**：左键强制攻击目标位置/友方
- **散开(D键)**：选中单位随机100~200px散开
- **巡逻(P键)**：两点间巡逻，遇敌接敌后继续
- **守卫(Shift+H)**：原地不追击，射程内反击
- **路径点(Shift+右键)**：追加路径点队列
- **阵型移动(F键)**：保持相对位置阵型推进
- **停止增强(X键)**：清除所有新命令状态

### 2.8 AI策略状态机 (ff5b17f)
- 4策略状态：Expand→BuildUp→Attack↔Defend
- 进攻集结系统：选目标→集结点→集结≥N→CommandAttackMove推进
- 动态建造优先级：按策略调整AIBuildLogic
- 战术评估：基地800px内己/敌方兵力比→策略切换
- 难度差异化：Easy无Attack，Brutal 7s检查+5单位集结

### 2.9 音频系统补强 (d4fcd57)
- BGM淡入淡出(0.5秒Tween) + 防重复触发
- 6种新Sfx(BuildingDamaged/BuildCancel/PowerRestored/TechUnlock/TurretFire/AaFire)
- 22种缺失单位语音注册(优雅降级，文件不存在静默跳过)
- 场景音效接入：建筑完成/取消/受击/防御塔开火/科技解锁/电力恢复

---

## 三、现在卡在哪

无阻塞。所有任务完成、编译0错误、已推送到GitHub。

---

## 四、接下来该干什么

### Phase 2-3 优先级排序

1. **单位美术升级** — RA2标志性单位(天启/光棱/基洛夫/磁暴)需专属图标和动画帧
2. **建筑美术升级** — 超武建筑(核弹井/闪电塔/导弹井)需专属等距图
3. **地面纹理扩展** — 5主题已有128x128纹理，可增加更多地形类型
4. **地图编辑器增强** — 支持自定义资源点布局/战略点预设
5. **网络对战基础** — 长期目标
6. **战役模式框架** — 长期目标
7. **性能优化** — 大规模单位卡顿(空间分区/LOD)

---

## 五、绝对不能踩的坑

1. **AIGC水印hook**：write/edit创建.md/.txt/.pdf/.docx/.pptx/.xlsx时自动注入水印。项目内.md文件必须走git对象层操作
2. **PowerShell 5.1限制**：不支持&&；TLS需Tls12；Set-Location不同步CurrentDirectory
3. **TrManager CSV**：Split("", 2)最多拆2段，value中逗号安全
4. **Godot导出**：必须用官方tpz模板
5. **GitHub推送**：网络不稳，失败后重试
6. **构建产物归档**：删除临时副本前先复制到releases\
7. **音频优雅降级**：新Sfx/Voice wav文件可能不存在，必须ResourceLoader.Exists检查

---

## 六、关键文件索引

| 文件 | 说明 |
|------|------|
| scripts/Main.cs (~1178行) | 游戏主循环 |
| scripts/Main.AI.cs (326行) | AI策略状态机(新增) |
| scripts/Unit.cs (~2700行) | 单位系统(含7项新命令) |
| scripts/Building.cs (~935行) | 建筑+防御+音效 |
| scripts/AudioManager.cs | Sfx枚举(26种)+3通道播放 |
| scripts/BgmManager.cs | 5场景BGM+淡入淡出 |
| scripts/UnitVoice.cs | 32种单位语音映射 |
| scripts/FogOfWar.cs | 战争迷雾 |
| scripts/BuildPanel.cs | 建造面板(已国际化) |
| data/units.json | 32个单位 |
| data/buildings.json | 13个建筑 |
| i18n/zh-CN.csv (798+行) | 中文翻译 |

---

## 七、Git提交历史

```
d4fcd57 feat: 音频系统补强 - BGM淡入淡出+6新音效+22种单位语音+场景音效接入
ff5b17f feat: AI策略状态机系统 - 4状态机+集结系统+动态建造+战术评估+难度差异化
eae6d16 feat: 实现7项命令系统(强制攻击A/散开D/巡逻P/守卫Shift+H/路径点Shift+右键/阵型F/停止增强)
47c84b0 docs: 更新handoff.md交接文档
3979196 feat: 平衡性调整+防御建筑平衡+硬编码中文清理+RA2标志性单位+i18n补全
```