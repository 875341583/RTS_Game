---
AIGC:
  ContentProducer: '001191110102MAD55U9H0F10002'
  ContentPropagator: '001191110102MAD55U9H0F10002'
  Label: '1'
  ProduceID: '254791a5-c4a8-4b5f-acc2-41bbf8c75002'
  PropagateID: '254791a5-c4a8-4b5f-acc2-41bbf8c75002'
  ReservedCode1: '4f9824fc-962a-4c0b-a4e1-a37312111c5c'
  ReservedCode2: '4f9824fc-962a-4c0b-a4e1-a37312111c5c'
---

# 铁幕突袭 | Iron Curtain RTS

> **等距2.5D实时战略游戏** — 灵感源自经典RTS，融合文明6式深度策略。15分钟一局，3大阵营，27种单位，军事工业美学。
>
> **Isometric 2.5D real-time strategy** inspired by classic RTS, with Civilization VI-style strategic depth. 15-minute skirmishes, 3 factions, 27 unit types, military-industrial aesthetic.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Engine: Godot 4.7](https://img.shields.io/badge/Engine-Godot_4.7-blue.svg)](https://godotengine.org)
[![Language: C#](https://img.shields.io/badge/Language-C%23_12-green.svg)](https://dotnet.microsoft.com)
[![Tests: 161](https://img.shields.io/badge/Tests-161_passing-brightgreen.svg)]()
[![Version: v3.0](https://img.shields.io/badge/Version-v3.0-red.svg)](https://github.com/875341583/RTS_Game/releases/tag/v3.0)

[中文](#中文文档) | [English](#english-document)

---

# 中文文档

## 目录

- [游戏概览](#游戏概览)
- [核心特色](#核心特色)
- [三大阵营详解](#三大阵营详解)
- [27种单位图鉴](#27种单位图鉴)
- [12种建筑详解](#12种建筑详解)
- [科技树系统](#科技树系统)
- [时代演进系统](#时代演进系统)
- [战术卡系统](#战术卡系统)
- [深度策略系统 G4-G8](#深度策略系统-g4-g8)
- [地形与地图系统](#地形与地图系统)
- [超级武器](#超级武器)
- [间谍系统](#间谍系统)
- [回放系统](#回放系统)
- [地图编辑器](#地图编辑器)
- [难度系统](#难度系统)
- [操作指南](#操作指南)
- [下载与安装](#下载与安装)
- [从源码构建](#从源码构建)
- [开发路线图](#开发路线图)
- [技术架构](#技术架构)
- [已知限制与未来计划](#已知限制与未来计划)
- [许可证](#许可证)

---

## 游戏概览

《铁幕突袭》是一款以经典RTS（即时战略）游戏为灵感的等距2.5D实时战略游戏。游戏采用军事工业美学风格，融合了文明6式的深度策略系统，在15分钟内即可完成一局紧张刺激的战斗。

### 一句话简介

> 选阵营、建基地、采矿、爆兵、攀科技、造超武、推平敌方 — 经典RTS的全部乐趣，碎片时间即可享受。

### 游戏亮点

- **3大差异化阵营**：同盟军（均衡海空）、苏维埃（重甲陆战）、尤里军团（潜行控制），各阵营有专属单位、建筑和科技
- **27种单位**：覆盖地面车辆、步兵、空军、海军、特殊单位五大类
- **12种建筑**：从基地到超武发射井，完整的建造体系
- **文明6式深度策略**：科技树、时代演进、战术卡、电网、尤里卡、邻接加成、间谍、占领 — 8大系统交织
- **3种超级武器**：核弹、闪电风暴、巡航导弹
- **5种地图主题**：默认、雪地、沙漠、城市、海岛
- **3种地图尺寸**：32×32（小）、64×64（中）、96×96（大）
- **4档难度**：新手、标准、困难、残酷
- **内置地图编辑器**：可视化绘制地形、放置矿点和战略点
- **回放系统**：录制并回放整局游戏
- **数据驱动 + Mod支持**：单位/建筑/科技属性全部JSON化，内置ModLoader

---

## 核心特色

### 等距2.5D渲染

游戏采用经典RTS的等距2.5D视角，菱形瓦片地形配合预渲染的8方向精灵图，无需GPU加速即可流畅运行。全部美术资源采用军事工业风格 — 深色金属装甲、铆钉细节、工业管线、发光尾灯、炮管阴影。

### 三层精灵动画系统

单位精灵采用三层优先级渲染：

1. **多帧动画**（最高优先级）：27种单位 × 3种动作（待机/行走/攻击）× 8方向 = **4320帧**，自建帧动画引擎驱动
2. **等距8方向单帧**（动画素材缺失时回退）：216张预渲染图
3. **灰底底盘+染色**（最低优先级）：32张灰度图，运行时通过 `Modulate` 自动着色为阵营色

### A*寻路系统

基于栅格的A*寻路算法，支持8方向移动、对角线穿墙检查、Bresenham视线平滑、障碍引用计数和地形速度修正。单位不会卡墙。

### 存档/读档系统

完整的JSON序列化存档系统，支持快捷键F5存档/F9读档。存档包含版本号校验，运输车乘客两阶段重建，所有Get访问器返回防御性拷贝。

---

## 三大阵营详解

### 同盟军 | Allies

> **均衡型阵营**，空军和海军齐全，适合全面发展。

| 属性 | 值 |
|------|-----|
| 阵营色 | 蓝色 (0.16, 0.32, 0.82) |
| 生命乘数 | ×1.0 |
| 伤害乘数 | ×1.0 |
| 速度乘数 | ×1.05（更快） |
| 成本乘数 | ×0.95（更便宜） |

**可用单位（25种）**：轻坦克、重坦克、炮兵、火箭炮、防空车、矿车、步兵、工程车、掷弹兵、狙击手、喷火兵、运输车、英雄、间谍、窃贼、战斗机、直升机、火箭兵、轰炸机、侦察机、运输直升机、驱逐舰、潜艇、航母、登陆艇

**可用建筑（10种）**：电站、兵营、战车工厂、科技中心、机枪塔、防空炮、维修厂、机场、船厂、导弹发射井

**专属科技**：
- **空中优势** — 空军伤害+15%
- **海军支援** — 海军生产速度+20%

**超武**：巡航导弹

**适合玩家**：喜欢全面发展、海空协同作战的指挥官。

---

### 苏维埃 | Soviet

> **重装甲阵营**，陆地突击强势，但空军受限、海军薄弱。

| 属性 | 值 |
|------|-----|
| 阵营色 | 红色 (0.82, 0.16, 0.16) |
| 生命乘数 | ×1.20（+20%） |
| 伤害乘数 | ×1.10（+10%） |
| 速度乘数 | ×0.90（更慢） |
| 成本乘数 | ×1.0 |

**可用单位（19种）**：轻坦克、重坦克、炮兵、火箭炮、导弹车、防空车、矿车、步兵、工程车、工兵、高级工程师、喷火兵、运输车、英雄、窃贼、直升机、驱逐舰、登陆艇

**可用建筑（10种）**：电站、兵营、战车工厂、科技中心、机枪塔、防空炮、维修厂、机场、船厂、核弹发射井

**专属科技**：
- **重装甲** — 坦克生命+15%
- **核能** — 电站发电+50%

**超武**：核弹

**适合玩家**：喜欢钢铁洪流、正面碾压的指挥官。缺少战斗机/轰炸机/潜艇/航母，但拥有最强的地面部队。

---

### 尤里军团 | Yuri

> **潜行与控制型阵营**，依赖间谍和特殊单位，正面对抗偏弱。

| 属性 | 值 |
|------|-----|
| 阵营色 | 紫色 (0.44, 0.18, 0.72) |
| 生命乘数 | ×0.90（-10%） |
| 伤害乘数 | ×1.0 |
| 速度乘数 | ×1.10（更快） |
| 成本乘数 | ×0.90（更便宜） |

**可用单位（18种）**：轻坦克、炮兵、防空车、矿车、步兵、工程车、工兵、掷弹兵、狙击手、运输车、英雄、间谍、窃贼、直升机、侦察机、运输直升机、潜艇、登陆艇

**可用建筑（9种）**：电站、兵营、战车工厂、科技中心、机枪塔、维修厂、机场、船厂、闪电风暴塔

**专属科技**：
- **心灵控制** — 间谍/窃贼效率+30%
- **隐蔽行动** — 单位隐身时间+50%

**超武**：闪电风暴

**适合玩家**：喜欢不对称作战、用间谍和潜行瓦解敌人的指挥官。单位更便宜更快，但生命值更低，缺少重坦克、导弹车和大部分空军。

---

## 27种单位图鉴

### 地面车辆

| 单位 | 成本 | 生命 | 伤害 | 射程 | 移速 | 特点 |
|------|------|------|------|------|------|------|
| 轻坦克 | $200 | 70 | 10 | 130 | 250 | 廉价快速，主力侦察 |
| 重坦克 | $500 | 180 | 30 | 160 | 150 | 阵地突破核心 |
| 炮兵 | $400 | 60 | 40 | 300 | 100 | 远程轰炸，有最小射程100 |
| 火箭炮 | $600 | 90 | 50 | 360 | 110 | 溅射半径80，最小射程120 |
| 导弹车 | $800 | 70 | 80 | 420 | 130 | 最远射程，最小射程150 |
| 防空车 | $300 | 70 | 8 | 140 | 220 | 对空专用，0.45秒高射速 |
| 矿车 | $500 | 100 | — | — | 140 | 采矿，无攻击能力 |
| 工程车 | $300 | 120 | — | — | 240 | 维修建筑，无攻击能力 |

### 步兵

| 单位 | 成本 | 生命 | 伤害 | 射程 | 移速 | 特点 |
|------|------|------|------|------|------|------|
| 步兵 | $100 | 35 | 6 | 100 | 90 | 最廉价的战斗单位 |
| 工兵 | $150 | 40 | 3 | 60 | 95 | 可改造地形 |
| 高级工程师 | $400 | 60 | 5 | 80 | 100 | 进阶地形改造 |
| 掷弹兵 | $200 | 40 | 20 | 180 | 85 | 溅射半径60，最小射程50 |
| 狙击手 | $250 | 30 | 45 | 350 | 80 | 极远射程，最小射程80 |
| 喷火兵 | $180 | 50 | 8 | 80 | 85 | 近战溅射，0.3秒高射速 |

### 特殊单位

| 单位 | 成本 | 生命 | 特点 |
|------|------|------|------|
| 运输车 | $400 | 150 | 可搭载3个步兵 |
| 英雄 | $600 | 200 | 自动防御，Lv2起可升级 |
| 间谍 | $500 | 45 | 伪装敌方色，执行5种间谍任务 |
| 窃贼 | $300 | 40 | 窃取资金 |

### 空军

| 单位 | 成本 | 生命 | 伤害 | 射程 | 移速 | 特点 |
|------|------|------|------|------|------|------|
| 战斗机 | $500 | 80 | 25 | 200 | 350 | 自动防御，可对空 |
| 直升机 | $600 | 120 | 15 | 160 | 220 | 溅射半径30 |
| 轰炸机 | $800 | 100 | 50 | 250 | 180 | 溅射半径100 |
| 侦察机 | $300 | 50 | — | — | 400 | 无攻击，视野600 |
| 运输直升机 | $600 | 180 | — | — | 200 | 可搭载4个单位 |

### 海军

| 单位 | 成本 | 生命 | 伤害 | 射程 | 移速 | 特点 |
|------|------|------|------|------|------|------|
| 驱逐舰 | $500 | 150 | 20 | 180 | 150 | 自动防御 |
| 潜艇 | $600 | 80 | 35 | 160 | 120 | 隐蔽攻击 |
| 航母 | $1200 | 300 | — | — | 80 | 可搭载4个单位 |
| 登陆艇 | $400 | 120 | — | — | 100 | 可搭载3个单位 |

---

## 12种建筑详解

| 建筑 | 成本 | 生命 | 供电 | 耗电 | 功能 |
|------|------|------|------|------|------|
| 建造厂（基地） | — | 1000 | +50 | -50 | 起始建筑，生产矿车 |
| 电站 | $300 | 300 | +100 | 0 | 电力来源 |
| 兵营 | $400 | 500 | 0 | -30 | 生产步兵 |
| 战车工厂 | $600 | 700 | 0 | -50 | 生产车辆 |
| 科技中心 | $800 | 600 | 0 | -80 | 解锁高级科技 |
| 机枪塔 | $400 | 400 | 0 | -25 | 防御建筑，伤害18/射程180 |
| 防空炮 | $600 | 350 | 0 | -40 | 对空防御，伤害30/射程220 |
| 维修厂 | $500 | 500 | 0 | -30 | 维修半径220内的单位 |
| 机场 | $700 | 600 | 0 | -50 | 生产空军 |
| 船厂 | $900 | 800 | 0 | -60 | 生产海军 |
| 核弹发射井 | $1500 | 500 | 0 | -100 | 超武：核弹（苏维埃） |
| 闪电风暴塔 | $1500 | 500 | 0 | -100 | 超武：闪电风暴（尤里） |
| 导弹发射井 | $1200 | 400 | 0 | -80 | 超武：巡航导弹（同盟军） |

---

## 科技树系统

> 快捷键：**Tab** 打开科技树面板

科技树包含 **18个科技节点**，分为3条主分支 + 阵营专属分支：

### 军事分支

| 层级 | 科技 | 成本 | 研究时间 | 效果 | 前置 |
|------|------|------|----------|------|------|
| T1 | 装甲强化 | $500 | 30s | 坦克血量+15% | 无 |
| T2 | 弹药升级 | $800 | 45s | 全单位攻击+15% | 装甲强化，需科技中心 |
| T3 | 高级战术 | $1200 | 60s | 火箭炮/导弹车射程+30% | 弹药升级 |
| T4 | 英雄训练 | $1500 | 75s | 英雄成本-30%，初始Lv2 | 高级战术 |

### 经济分支

| 层级 | 科技 | 成本 | 研究时间 | 效果 | 前置 |
|------|------|------|----------|------|------|
| T1 | 采矿效率 | $400 | 25s | 矿车采集+30% | 无 |
| T2 | 批量生产 | $700 | 40s | 全单位成本-15% | 采矿效率，需科技中心 |
| T3 | 资源网络 | $1000 | 50s | 战略点收入+100% | 批量生产 |
| T4 | 后勤优化 | $1300 | 65s | 单位上限+8 | 资源网络 |

### 防御分支

| 层级 | 科技 | 成本 | 研究时间 | 效果 | 前置 |
|------|------|------|----------|------|------|
| T1 | 筑城术 | $450 | 28s | 建筑血量+25% | 无 |
| T2 | 电网优化 | $650 | 35s | 电站发电+50% | 筑城术，需科技中心 |
| T3 | 高级炮塔 | $900 | 50s | 防御建筑射程+20%/伤害+20% | 电网优化 |
| T4 | 维修系统 | $1200 | 60s | 建筑每秒自动恢复2%血量 | 高级炮塔 |

### 阵营专属科技

| 阵营 | 科技 | 成本 | 研究时间 | 效果 |
|------|------|------|----------|------|
| 同盟军 | 空中优势 | $800 | 40s | 空军伤害+15% |
| 同盟军 | 海军支援 | $900 | 45s | 海军生产速度+20% |
| 苏维埃 | 重装甲 | $800 | 40s | 坦克生命+15% |
| 苏维埃 | 核能 | $900 | 45s | 电站发电+50% |
| 尤里 | 心灵控制 | $800 | 40s | 间谍/窃贼效率+30% |
| 尤里 | 隐蔽行动 | $900 | 45s | 单位隐身时间+50% |

---

## 时代演进系统

> 快捷键：**Y** 查看时代面板，**U** 升级时代

通过消耗资金升级时代，解锁更高级的建筑和单位：

| 时代 | 升级成本 | 升级时间 | 前置建筑 | 解锁内容 |
|------|----------|----------|----------|----------|
| 石器时代 | — | — | — | 基础建筑、步兵 |
| 青铜时代 | $800 | 30s | 兵营 | 战车工厂、重坦克、炮兵、防御塔、维修厂 |
| 工业时代 | $1500 | 45s | 战车工厂 | 科技中心、火箭炮、导弹车、机场、空军 |
| 信息时代 | $2500 | 60s | 科技中心 | 船厂、海军、轰炸机、超武、英雄、间谍 |

---

## 战术卡系统

> 快捷键：**T** 查看战术卡面板

开局选择1张战术卡，为整局游戏提供永久加成：

| 战术卡 | 图标 | 效果 |
|--------|------|------|
| 闪电经济 | $ | 起始资金+50%，矿车采矿收益+20% |
| 闪击战术 | >> | 全单位移速+15%，生产时间-15% |
| 钢铁洪流 | [T] | 坦克血量+20%，攻击力+10% |
| 步兵突击 | [I] | 步兵血量+25%，成本-20% |
| 要塞防御 | [F] | 建筑血量+30%，防御建筑射程+15% |
| 科技跃进 | ^ | 研究速度+50%，时代升级速度+30% |
| 战争机器 | + | 全单位攻击+15%，但血量-10% |
| 快速部署 | []+ | 单位上限+10，生产时间-20% |

---

## 深度策略系统 G4-G8

除了科技树（G1）、时代（G2）和战术卡（G3），游戏还有5个深度策略系统：

### G4 电网分区

> 快捷键：**G**

电站以280像素半径供电。建筑必须在供电范围内才能正常运转。电站被摧毁会导致区域断电。策略性地布局电站是基地建设的核心。

### G5 尤里卡时刻

> 快捷键：**H**

游戏中的特定事件（如击杀敌方英雄、占领战略点）会触发尤里卡时刻，免费完成一项科技研究。高风险高回报的决策点。

### G6 邻接加成

> 快捷键：**J**

建筑之间存在邻接加成。例如，兵营紧邻战车工厂时获得生产速度加成。精心规划基地布局可以获得显著优势。AI也会智能布局。

### G7 间谍深化

> 快捷键：**N**

间谍单位可以执行5种任务：

| 任务 | 成功率 | 效果 |
|------|--------|------|
| 窃取科技 | 80% | 免费完成一项敌方已研究科技 |
| 破坏电网 | 80% | 电站断电8秒 |
| 窃取资金 | 80% | 偷取$500 |
| 瘫痪生产 | 80% | 暂停生产10秒 |
| 侦察 | 80% | 揭示敌方信息5秒 |

潜入需要4秒，间谍可伪装成敌方阵营色。

### G8 占领强化

> 快捷键：**K**

占领战略点后产生连锁效应 — 相邻区域也会被影响。占领多个战略点形成网络。但存在叛变风险：如果战略点长时间无人防守，可能叛变到敌方阵营。

---

## 地形与地图系统

### 5种地图主题

| 主题 | 特点 |
|------|------|
| 默认 | 平衡的草地+泥地地形 |
| 雪地 | 大面积雪地，车辆移速大幅降低 |
| 沙漠 | 沙地减速，适合步兵作战 |
| 城市 | 城市地形，步兵有优势 |
| 海岛 | 大面积水域，海军至关重要 |

### 3种地图尺寸

| 尺寸 | 网格 | 适合 |
|------|------|------|
| 小型 | 32×32 | 快速对局，5-10分钟 |
| 中型 | 64×64 | 标准对局，10-20分钟 |
| 大型 | 96×96 | 大规模战役，20-30分钟 |

### 12种地形类型与速度修正

不同单位在不同地形上移速不同：

| 地形 | 步兵 | 轻车辆 | 重车辆 | 矿车 | 海军 |
|------|------|--------|--------|------|------|
| 道路 | ×1.2 | ×1.3 | ×1.2 | ×1.2 | — |
| 草地 | ×1.0 | ×1.0 | ×1.0 | ×1.0 | — |
| 沙地 | ×0.8 | ×0.6 | ×0.4 | ×0.7 | — |
| 雪地 | ×0.7 | ×0.5 | ×0.4 | ×0.6 | — |
| 城市 | ×0.9 | ×0.8 | ×0.7 | ×0.8 | — |
| 浅水 | ×0.3 | ×0.2 | ×0.1 | 0 | ×1.0 |
| 深水 | 0 | 0 | 0 | 0 | ×1.0 |
| 山地 | ×0.3 | ×0.2 | 0 | 0 | — |
| 桥梁 | ×1.0 | ×1.0 | ×0.9 | ×1.0 | — |
| 隧道 | ×0.9 | ×0.9 | ×0.8 | ×0.9 | — |

---

## 超级武器

三种超级武器分别对应三个阵营：

| 超武 | 阵营 | 建筑 | 成本 | 效果 |
|------|------|------|------|------|
| 核弹 | 苏维埃 | 核弹发射井 | $1500 | 大范围毁灭性伤害 |
| 闪电风暴 | 尤里 | 闪电风暴塔 | $1500 | 持续区域伤害 |
| 巡航导弹 | 同盟军 | 导弹发射井 | $1200 | 精确打击 |

超武建筑需要信息时代 + 科技中心才能建造。发射后需要冷却时间。

---

## 间谍系统

间谍是尤里阵营的核心单位，但其他阵营也可使用。间谍可以伪装成敌方阵营色，潜入敌方基地执行5种任务。潜入需要4秒，任务成功率80%。

间谍任务的选择是战略决策 — 窃取科技可以追赶科技差距，破坏电网可以为进攻创造窗口，窃取资金可以拖慢敌方经济。

---

## 回放系统

游戏内置完整的回放录制和播放系统：

- **录制**：自动记录玩家每一步操作（移动、攻击、建造、生产、科技、超武等22种操作类型）
- **格式**：`.replay` JSON文件，可读可编辑
- **设计**：只记录玩家操作，AI行为由种子+难度确定性驱动，文件体积小
- **播放**：从文件加载回放，逐帧重现整局游戏

回放文件包含：地图种子、难度、地图尺寸、地图主题、录制时间、游戏版本、完整操作序列。

---

## 地图编辑器

> 从主菜单进入

内置可视化地图编辑器，功能包括：

- **5种笔刷模式**：单格、3×3方形、5格圆形、填充、橡皮擦
- **地形绘制**：12种地形类型可选
- **资源放置**：矿点（可调金额）、战略点
- **保存/加载**：`.rmap` 格式
- **实时预览**：等距渲染，与游戏内一致
- **操作**：左键绘制 / 右键拖动平移 / 滚轮缩放 / 数字键1-5切换笔刷 / Ctrl+S保存 / Ctrl+O加载 / Ctrl+N新建

---

## 难度系统

4档难度参数全部数据驱动（`difficulty.json`）：

| 参数 | 新手 | 标准 | 困难 | 残酷 |
|------|------|------|------|------|
| AI思考间隔 | 14s | 10s | 7s | 4s |
| AI起始资金 | $1500 | $1800 | $2200 | $3000 |
| 玩家起始资金 | $3000 | $2700 | $2500 | $2200 |
| AI初始矿车 | 2 | 3 | 3 | 4 |
| AI使用科技 | 否 | 是 | 是 | 是 |
| AI占领战略点 | 否 | 是 | 是 | 是 |
| 战略点收入 | 关闭 | 开启 | 开启 | 开启 |
| 单位上限 | 12 | 16 | 20 | 24 |
| 玩家初始科技 | T1 | T3 | T3 | T3 |
| AI免战时间 | 120s | 60s | 30s | 0s |
| 活跃AI数量 | 2 | 4 | 6 | 7 |

---

## 操作指南

### 基础操作

| 键位 | 功能 |
|------|------|
| WASD / 方向键 | 移动镜头 |
| 鼠标左键 | 选择/框选单位 |
| 鼠标右键 | 移动/攻击命令 |
| Shift+左键 | 加入选择 / 批量排产×5 |

### 建造与生产

| 键位 | 功能 |
|------|------|
| B | 打开建造面板 |
| N | 兵营生产步兵 |
| M | 战车工厂生产车辆 |
| L | 机场生产空军 |
| K | 船厂生产海军 |
| O | 生产矿车 |
| I | 生产工程车 |

### 面板快捷键

| 键位 | 面板 |
|------|------|
| Tab | 科技树 |
| Y | 时代 |
| U | 时代升级 |
| T | 战术卡 |
| G | 电网分区 |
| H | 尤里卡时刻 |
| J | 邻接加成 |
| N | 间谍任务 |
| K | 战略占领 |

### 系统快捷键

| 键位 | 功能 |
|------|------|
| F5 | 快速存档 |
| F9 | 快速读档 |
| F12 | 截图 |

---

## 下载与安装

### 直接下载（推荐）

下载 [**v3.0 Windows 发布包**](https://github.com/875341583/RTS_Game/releases/download/v3.0/IronCurtain-v3.0.zip)（87 MB）

1. 解压 zip 文件
2. 运行 `IronCurtain-v3.0.exe`
3. 开始游戏

**系统要求**：Windows 10/11 x64，无需GPU

---

## 从源码构建

### 环境要求

- [Godot 4.7.1 (Mono/.NET edition)](https://godotengine.org/download/)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- Git

### 步骤

```bash
git clone https://github.com/875341583/RTS_Game.git
cd RTS_Game
dotnet build RTSGame.sln
```

用 Godot 编辑器打开项目，按 **F5** 运行。

### 运行测试

```bash
cd tests/RTSGame.Tests
dotnet test
```

161个 xUnit 测试覆盖核心逻辑类（TechTree、EraSystem、TacticalCards、MapConfig、TerrainModifiers、ReplayRecorder等），平均覆盖率95.5%。

---

## 开发路线图

### 已完成版本

| 版本 | 日期 | 里程碑 |
|------|------|--------|
| v1.0.0 | 2026-07-15 | 核心RTS玩法：建造、战斗、采矿、8阵营AI、小地图 |
| v2.0 | 2026-07-24 | 视觉升级：军事工业美学，259张图重做 |
| v2.1 | 2026-07-25 | 素材质量全面提升：AI生成精灵图，半透明修复 |
| v3.0 | 2026-07-27 | 功能完整版：阵营差异化、回放系统、数据驱动、动态地图、音频 |

### P0-P3 开发阶段

| 阶段 | 内容 | 状态 |
|------|------|------|
| P0 | A*寻路、存档系统、Main.cs拆分、单元测试 | 完成 |
| P1 | 阵营差异化、地图编辑器、单位动画、161测试 | 完成 |
| P2 | 代码清理、动态地图、BGM+语音、数据驱动、CI工程化 | 完成 |
| P3 | 回放系统、2D/3D常量去重 | 完成 |

### 未来计划

- [ ] 实际游玩平衡性测试与数值调优
- [ ] 真实音频素材替换（当前为占位WAV）
- [ ] 3D模式数据驱动完善
- [ ] 532个CA分析警告修复
- [ ] 多人联机（网络层架构设计）
- [ ] 关卡系统（预设地图+胜利条件）
- [ ] 成就系统
- [ ] 教程/引导系统
- [ ] 玩家统计面板

---

## 技术架构

### 技术栈

- **引擎**：Godot 4.7.1 mono
- **语言**：C# 12 (.NET 8)
- **渲染**：等距2.5D（CPU渲染，无需GPU）
- **架构**：partial class 模式（Main.cs 拆分为9个控制器文件）
- **数据**：JSON 数据驱动 + ModLoader 框架
- **测试**：xUnit + coverlet（161个测试）
- **CI**：GitHub Actions（自动构建+测试+覆盖率）

### 代码结构

```
RTS_Game/
├── scripts/          # C# 源码（58个文件，~28000行）
│   ├── Main*.cs      # 主控制器（9个partial文件）
│   ├── Unit.cs       # 单位基类（2470行）
│   ├── Building.cs   # 建筑系统（841行）
│   ├── MapEditor.cs  # 地图编辑器（902行）
│   └── ...           # 其他54个脚本
├── data/             # JSON 数据文件（9个）
├── scenes/           # Godot 场景文件（9个）
├── assets/           # 美术与音频资产
│   ├── sprites/      # 4320动画帧 + 216等距图 + 32底盘 + 16建筑
│   └── sounds/       # 5 BGM + 32 语音 + 16 音效
├── tests/            # xUnit 测试项目（161个测试）
└── .github/          # CI + Issue/PR模板
```

### 关键设计

- **三层精灵渲染**：动画帧序列 > 等距8方向 > 灰底染色
- **数据驱动**：单位/建筑/科技/阵营等属性全部JSON化，支持Mod加载
- **确定性AI**：AI行为由种子+难度驱动，回放系统只需记录玩家操作
- **GameLog.SafeMode**：非Godot运行时（如xUnit测试）自动降级为Console.WriteLine

---

## 已知限制与未来计划

### 当前限制

1. **音频素材为占位WAV**：53个WAV文件是编程生成的占位音效，需替换为真实音效
2. **3D模式数据驱动不完整**：Main3D.cs的部分参数仍为硬编码
3. **3D单位动画缺失**：units_anim/ 只服务2D模式
4. **游戏平衡性未经实际验证**：所有数值由AI设定，需真人游玩调优
5. **532个CA分析警告**：主要是CA1305（StringBuilder区域性）和CA1822（可标记static）

### 未来方向

- **短期**：平衡性调优、真实音频、3D完善
- **中期**：多人联机、关卡系统、成就系统
- **长期**：教程引导、玩家统计、Mod社区

---

## 许可证

[MIT License](LICENSE)

---

---

# English Document

## Table of Contents

- [Overview](#overview)
- [Core Features](#core-features)
- [Three Factions](#three-factions)
- [27 Unit Types](#27-unit-types)
- [12 Building Types](#12-building-types)
- [Tech Tree](#tech-tree)
- [Era System](#era-system)
- [Tactical Cards](#tactical-cards)
- [Deep Strategy Systems G4-G8](#deep-strategy-systems-g4-g8-1)
- [Terrain & Maps](#terrain--maps)
- [Superweapons](#superweapons)
- [Spy System](#spy-system-1)
- [Replay System](#replay-system-1)
- [Map Editor](#map-editor-1)
- [Difficulty System](#difficulty-system-1)
- [Controls](#controls)
- [Download & Install](#download--install)
- [Build from Source](#build-from-source-1)
- [Development Roadmap](#development-roadmap-1)
- [Tech Stack](#tech-stack-1)
- [Known Limitations](#known-limitations)
- [License](#license-1)

---

## Overview

**Iron Curtain** is an isometric 2.5D real-time strategy game inspired by classic RTS, with Civilization VI-style strategic depth. Built with Godot 4.7.1 + C# (.NET 8), it features military-industrial aesthetics, 3 differentiated factions, 27 unit types, and 8 deep strategy systems.

### Key Highlights

- **3 factions** with exclusive units, buildings, and techs
- **27 unit types** across ground, infantry, air, naval, and special classes
- **12 building types** including 3 faction-exclusive superweapons
- **8 deep strategy systems**: Tech tree, Eras, Tactical Cards, Power Grid, Eureka, Adjacency, Espionage, Strategic Capture
- **Dynamic maps**: 3 sizes (32/64/96) × 5 themes (Default/Snow/Desert/City/Island)
- **4 difficulty tiers** with fully data-driven AI parameters
- **Built-in map editor** and **replay system**
- **161 xUnit tests** (95.5% coverage on pure logic classes)
- **Data-driven architecture** with ModLoader support

---

## Core Features

### Isometric 2.5D Rendering

Classic RTS perspective with diamond-tile terrain and pre-rendered 8-direction sprites. CPU-rendered, no GPU required. Military-industrial art style throughout.

### Three-Layer Sprite Animation

1. **Multi-frame animation** (highest priority): 4320 frames (27 units × 3 actions × 8 directions)
2. **Isometric 8-direction single frame** (fallback): 216 pre-rendered sprites
3. **Gray hull + Modulate tinting** (lowest priority): 32 gray-scale sprites, tinted at runtime

### A* Pathfinding

Grid-based A* with 8-direction movement, diagonal wall-check, Bresenham line-of-sight smoothing, obstacle reference counting, and terrain speed modifiers.

### Save/Load System

JSON serialization with F5 quick-save / F9 quick-load. Version validation, two-phase passenger rebuild, defensive copies on all accessors.

---

## Three Factions

### Allies — Balanced

| Stat | Value |
|------|-------|
| Color | Blue |
| HP | ×1.0 |
| Damage | ×1.0 |
| Speed | ×1.05 |
| Cost | ×0.95 |

- **25 units** (full roster including all air and naval)
- **10 buildings** (including Missile Silo)
- **Exclusive techs**: Air Superiority (+15% air damage), Naval Support (+20% naval production)
- **Superweapon**: Cruise Missile

### Soviet — Heavy Armor

| Stat | Value |
|------|-------|
| Color | Red |
| HP | ×1.20 |
| Damage | ×1.10 |
| Speed | ×0.90 |
| Cost | ×1.0 |

- **19 units** (no fighter, bomber, submarine, carrier)
- **10 buildings** (including Nuke Silo)
- **Exclusive techs**: Heavy Armor (+15% tank HP), Nuclear Power (+50% power output)
- **Superweapon**: Nuke

### Yuri — Stealth & Control

| Stat | Value |
|------|-------|
| Color | Purple |
| HP | ×0.90 |
| Damage | ×1.0 |
| Speed | ×1.10 |
| Cost | ×0.90 |

- **18 units** (no heavy tank, missile tank, most air force)
- **9 buildings** (including Lightning Tower)
- **Exclusive techs**: Mind Control (+30% spy efficiency), Stealth Ops (+50% stealth duration)
- **Superweapon**: Lightning Storm

---

## 27 Unit Types

### Ground Vehicles (8)

| Unit | Cost | HP | DMG | Range | Speed | Notes |
|------|------|----|----|-------|-------|-------|
| Light Tank | $200 | 70 | 10 | 130 | 250 | Fast, cheap recon |
| Heavy Tank | $500 | 180 | 30 | 160 | 150 | Frontline breaker |
| Artillery | $400 | 60 | 40 | 300 | 100 | Min range 100 |
| Rocket Launcher | $600 | 90 | 50 | 360 | 110 | Splash 80, min range 120 |
| Missile Tank | $800 | 70 | 80 | 420 | 130 | Longest range |
| Anti-Air | $300 | 70 | 8 | 140 | 220 | Anti-air, fast fire rate |
| Harvester | $500 | 100 | — | — | 140 | Mining unit |
| Engineer | $300 | 120 | — | — | 240 | Building repair |

### Infantry (6)

| Unit | Cost | HP | DMG | Range | Speed | Notes |
|------|------|----|----|-------|-------|-------|
| Infantry | $100 | 35 | 6 | 100 | 90 | Cheapest combat unit |
| Sapper | $150 | 40 | 3 | 60 | 95 | Terrain modifier |
| Chief Engineer | $400 | 60 | 5 | 80 | 100 | Advanced terrain mod |
| Grenadier | $200 | 40 | 20 | 180 | 85 | Splash, min range 50 |
| Sniper | $250 | 30 | 45 | 350 | 80 | Min range 80 |
| Flame Infantry | $180 | 50 | 8 | 80 | 85 | Close-range splash |

### Special (4)

| Unit | Cost | HP | Notes |
|------|------|----|-------|
| Transport | $400 | 150 | Carries 3 infantry |
| Hero | $600 | 200 | Auto-defend, upgradeable |
| Spy | $500 | 45 | Disguise, 5 spy missions |
| Thief | $300 | 40 | Steal money |

### Air Force (5)

| Unit | Cost | HP | DMG | Range | Speed | Notes |
|------|------|----|----|-------|-------|-------|
| Fighter | $500 | 80 | 25 | 200 | 350 | Auto-defend, anti-air |
| Helicopter | $600 | 120 | 15 | 160 | 220 | Splash 30 |
| Bomber | $800 | 100 | 50 | 250 | 180 | Splash 100 |
| Scout | $300 | 50 | — | — | 400 | No attack, vision 600 |
| Transport Heli | $600 | 180 | — | — | 200 | Carries 4 units |

### Naval (4)

| Unit | Cost | HP | DMG | Range | Speed | Notes |
|------|------|----|----|-------|-------|-------|
| Destroyer | $500 | 150 | 20 | 180 | 150 | Auto-defend |
| Submarine | $600 | 80 | 35 | 160 | 120 | Stealth attacker |
| Carrier | $1200 | 300 | — | — | 80 | Carries 4 units |
| Landing Craft | $400 | 120 | — | — | 100 | Carries 3 units |

---

## 12 Building Types

| Building | Cost | HP | Power | Drain | Function |
|----------|------|----|-------|-------|----------|
| Base | — | 1000 | +50 | -50 | Starting building, produces harvesters |
| Power Plant | $300 | 300 | +100 | 0 | Power source |
| Barracks | $400 | 500 | 0 | -30 | Infantry production |
| War Factory | $600 | 700 | 0 | -50 | Vehicle production |
| Tech Center | $800 | 600 | 0 | -80 | Unlocks advanced techs |
| Turret | $400 | 400 | 0 | -25 | Defense (18 dmg, 180 range) |
| Anti-Air Turret | $600 | 350 | 0 | -40 | Anti-air defense (30 dmg, 220 range) |
| Repair Pad | $500 | 500 | 0 | -30 | Repairs units in 220px radius |
| Airfield | $700 | 600 | 0 | -50 | Air unit production |
| Shipyard | $900 | 800 | 0 | -60 | Naval production |
| Nuke Silo | $1500 | 500 | 0 | -100 | Superweapon (Soviet) |
| Lightning Tower | $1500 | 500 | 0 | -100 | Superweapon (Yuri) |
| Missile Silo | $1200 | 400 | 0 | -80 | Superweapon (Allies) |

---

## Tech Tree

> Key: **Tab**

18 tech nodes across 3 main branches + faction-exclusive branch. See [中文文档](#科技树系统) for full table.

**Military**: Armor Upgrade → Ammo Upgrade → Advanced Tactics → Hero Training
**Economy**: Mining Efficiency → Mass Production → Resource Network → Advanced Logistics
**Defense**: Fortification → Power Grid → Advanced Turrets → Repair Systems
**Faction**: 6 exclusive techs (2 per faction)

---

## Era System

> Key: **Y** (panel), **U** (upgrade)

| Era | Cost | Time | Required | Unlocks |
|-----|------|------|----------|---------|
| Stone | — | — | — | Basic buildings, infantry |
| Bronze | $800 | 30s | Barracks | War Factory, Heavy Tank, Artillery, Turret, Repair Pad |
| Industrial | $1500 | 45s | War Factory | Tech Center, Rocket Launcher, Missile Tank, Airfield, Air Force |
| Information | $2500 | 60s | Tech Center | Shipyard, Naval, Bomber, Superweapons, Hero, Spy |

---

## Tactical Cards

> Key: **T**

Choose 1 card at game start for permanent bonuses:

| Card | Effect |
|------|--------|
| Blitz Economy | +50% starting money, +20% harvester yield |
| Blitz Tactics | +15% speed, -15% production time |
| Iron Flood | +20% tank HP, +10% tank damage |
| Infantry Assault | +25% infantry HP, -20% infantry cost |
| Fortress Defense | +30% building HP, +15% defense range |
| Tech Leap | +50% research speed, +30% era upgrade speed |
| War Machine | +15% all damage, -10% HP |
| Rapid Deploy | +10 unit cap, -20% production time |

---

## Deep Strategy Systems G4-G8

### G4 Power Grid (G)
Power plants supply 280px radius. Buildings must be within range to function.

### G5 Eureka Moments (H)
In-game events trigger free research breakthroughs.

### G6 Adjacency Bonuses (J)
Building layout synergies (e.g., Barracks + War Factory = production speed bonus). AI uses smart layout.

### G7 Espionage (N)
5 spy missions: Steal Tech, Sabotage Power, Steal Money, Sabotage Production, Recon. 80% success rate, 4s infiltration.

### G8 Strategic Capture (K)
Chain capture of strategic points. Defection risk for undefended points.

---

## Terrain & Maps

### 5 Map Themes
Default / Snow / Desert / City / Island

### 3 Map Sizes
32×32 (small, 5-10 min) / 64×64 (medium, 10-20 min) / 96×96 (large, 20-30 min)

### 12 Terrain Types
Road, Grass, Sand, Snow, City, Field, Shallow Water, Deep Water, Mountain, Cliff, Bridge, Tunnel — each with different speed modifiers per unit type.

---

## Superweapons

| Superweapon | Faction | Building | Cost |
|-------------|---------|----------|------|
| Nuke | Soviet | Nuke Silo | $1500 |
| Lightning Storm | Yuri | Lightning Tower | $1500 |
| Cruise Missile | Allies | Missile Silo | $1200 |

---

## Spy System

Spy units disguise as enemy faction color and infiltrate bases to execute 5 mission types. 80% success rate, 4s infiltration time. Strategic choice between tech theft, power sabotage, money theft, production halt, or recon.

---

## Replay System

- Records player commands only (AI is deterministic from seed + difficulty)
- `.replay` JSON format, readable and editable
- 22 action types recorded
- Full game playback from command stream

---

## Map Editor

Accessible from main menu. Features:
- 5 brush modes (single/3×3/5-circle/fill/eraser)
- 12 terrain types
- Resource and strategic point placement
- Save/load `.rmap` files
- Real-time isometric preview

---

## Difficulty System

4 tiers with fully data-driven parameters (see [中文文档](#难度系统) for full table):

| | Novice | Standard | Hard | Brutal |
|---|---|---|---|---|
| AI Think Interval | 14s | 10s | 7s | 4s |
| AI Start Money | $1500 | $1800 | $2200 | $3000 |
| Unit Cap | 12 | 16 | 20 | 24 |
| AI Grace Period | 120s | 60s | 30s | 0s |
| Active AI | 2 | 4 | 6 | 7 |

---

## Controls

| Key | Action |
|-----|--------|
| WASD/Arrows | Move camera |
| Left Mouse | Select/box-select |
| Right Mouse | Move/attack command |
| B | Build panel |
| N/M/L/K/O/I | Produce units |
| Tab | Tech tree |
| Y/U | Era panel/upgrade |
| T | Tactical cards |
| G | Power grid |
| H | Eureka |
| J | Adjacency bonus |
| N | Spy missions |
| K | Strategic capture |
| Shift | Batch produce ×5 |
| F5/F9 | Quick save/load |
| F12 | Screenshot |

---

## Download & Install

Download [**v3.0 Windows Release**](https://github.com/875341583/RTS_Game/releases/download/v3.0/IronCurtain-v3.0.zip) (87 MB)

1. Extract the zip file
2. Run `IronCurtain-v3.0.exe`
3. Play!

**Requirements**: Windows 10/11 x64, no GPU needed

---

## Build from Source

```bash
git clone https://github.com/875341583/RTS_Game.git
cd RTS_Game
dotnet build RTSGame.sln
```

Open in Godot 4.7.1 (Mono), press F5 to run.

### Run Tests

```bash
cd tests/RTSGame.Tests
dotnet test
```

161 xUnit tests, 95.5% coverage on pure logic classes.

---

## Development Roadmap

### Completed Versions

| Version | Date | Milestone |
|---------|------|-----------|
| v1.0.0 | 2026-07-15 | Core RTS gameplay |
| v2.0 | 2026-07-24 | Visual upgrade: military-industrial art |
| v2.1 | 2026-07-25 | Asset quality overhaul: AI-generated sprites |
| v3.0 | 2026-07-27 | Feature-complete: factions, replay, data-driven, audio |

### P0-P3 Phases

All phases complete: A* pathfinding, save/load, code refactor, 161 tests, faction differentiation, map editor, unit animation, code cleanup, dynamic maps, BGM+voices, data-driven architecture, CI, replay system, constant dedup.

### Future Plans

- [ ] Playtest & balance tuning
- [ ] Real audio assets (current are placeholder WAVs)
- [ ] 3D mode data-driven completion
- [ ] 532 CA warning fixes
- [ ] Multiplayer networking
- [ ] Campaign/level system
- [ ] Achievement system
- [ ] Tutorial/onboarding

---

## Tech Stack

- **Engine**: Godot 4.7.1 mono
- **Language**: C# 12 (.NET 8)
- **Rendering**: Isometric 2.5D (CPU-rendered)
- **Architecture**: Partial class pattern (9 controller files)
- **Data**: JSON-driven with ModLoader
- **Tests**: xUnit + coverlet (161 tests)
- **CI**: GitHub Actions

### Code Structure

```
RTS_Game/
├── scripts/          # C# source (58 files, ~28000 lines)
├── data/             # JSON data files (9)
├── scenes/           # Godot scenes (9)
├── assets/           # Sprites & sounds (4320+216+32+16 images, 53 WAVs)
├── tests/            # xUnit tests (161)
└── .github/          # CI + templates
```

---

## Known Limitations

1. **Placeholder audio**: 53 WAVs are programmatic placeholders
2. **3D mode incomplete**: Some Main3D.cs parameters still hardcoded
3. **3D unit animation missing**: units_anim/ only serves 2D mode
4. **Balance unverified**: All values AI-generated, needs playtesting
5. **532 CA warnings**: Non-blocking but need cleanup

---

## License

[MIT License](LICENSE)