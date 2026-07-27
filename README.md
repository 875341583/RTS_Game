# Iron Curtain RTS (铁幕突袭)

> Isometric 2.5D real-time strategy game inspired by classic RTS, built with Godot 4.7.1 + C# (.NET 8).
> 15-minute skirmishes. 3 factions. Deep strategy. Military-industrial aesthetic.

---

## Features

### Core RTS Gameplay
- Isometric 2.5D rendering (diamond-tile terrain + pre-rendered 8-direction sprites)
- 3 differentiated factions (Allies / Soviet / Yuri) with exclusive units, buildings, and techs
- 27 unit types: tanks, infantry, aircraft, naval, plus harvesters, spies, and engineers
- 12 building types: base, power plant, barracks, war factory, tech center, and superweapons
- 4 difficulty tiers (Novice / Standard / Hard / Brutal) with data-driven AI parameters

### Civilization-Inspired Depth
| System | Key | Description |
|--------|-----|-------------|
| G1 Tech Tree | Tab | 18-node 3-branch research tree, faction-exclusive techs |
| G2 Era System | Y/U | 4 eras with escalating bonuses and unit unlocks |
| G3 Tactical Cards | T | 8 cards for opening strategy (production, speed, combat bonuses) |
| G4 Power Grid | G | Power plant radius coverage, strategic building placement |
| G5 Eureka Moments | H | In-game events trigger free research breakthroughs |
| G6 Adjacency Bonuses | J | Building layout synergies (factory+barracks, etc.) |
| G7 Espionage | N | 5 spy mission types (sabotage, intel, tech theft...) |
| G8 Strategic Capture | K | Chain capture of strategic points + defection risk |

### Engineering Highlights
- A* pathfinding on procedural terrain (no wall-stuck, 8-direction movement)
- Save/Load system with version migration (F5 quick-save / F9 quick-load)
- Replay system: record and replay full games from command streams
- Map Editor: visual terrain painting, resource/strategic point placement, save/load `.rmap`
- Data-driven architecture: unit/building/faction stats in JSON, with ModLoader support
- 161 xUnit tests (95.5% coverage on pure logic classes)
- Dynamic map sizes (32/64/96) with 5 terrain themes (Default / Snow / Desert / City / Island)
- Unit animation: 4320 frames (27 units x 3 actions x 8 directions), custom frame animation engine
- BGM + unit voice system (5 scene BGMs, 10 unit voice sets)

---

## Quick Start

### Requirements
- Windows (Linux/macOS experimental)
- **Pre-built**: Just download and run `IronCurtain-v3.0.exe`
- **From source**: Godot 4.7.1 (Mono/.NET edition) + .NET 8 SDK

### Build from Source
```bash
git clone https://github.com/875341583/RTS_Game.git
cd RTS_Game
dotnet build RTSGame.sln
```

Open in Godot editor, press F5 to run.

### Download Release
[**v3.0 Windows Release**](https://github.com/875341583/RTS_Game/releases/download/v3.0/IronCurtain-v3.0.zip) (87 MB)

---

## Controls

| Key | Action |
|-----|--------|
| WASD / Arrows | Move camera |
| Left Mouse | Select / box-select units |
| Right Mouse | Move / attack command |
| B / N / M / L / K / O / I | Produce units / buildings |
| Tab | Tech tree panel |
| Y / U | Era panel / upgrade |
| T | Tactical cards panel |
| G | Power grid panel |
| H | Eureka panel |
| J | Adjacency bonus panel |
| N | Spy mission panel |
| K | Strategic capture panel |
| Shift | Batch produce x5 |
| F5 / F9 | Quick save / quick load |
| F12 | Screenshot |

---

## Testing

```bash
cd tests/RTSGame.Tests
dotnet test
```

161 tests covering pure logic classes (TechTree, EraSystem, TacticalCards, MapConfig, TerrainModifiers, ReplayRecorder, etc.).

---

## Tech Stack

- **Engine**: Godot 4.7.1 mono
- **Language**: C# 12 (.NET 8)
- **Rendering**: Isometric 2.5D (CPU-rendered, GPU optional)
- **Architecture**: Partial class pattern (Main.cs split into 9 controller files)
- **Data**: JSON-driven with ModLoader framework
- **Tests**: xUnit + coverlet
- **CI**: GitHub Actions (build + test + coverage)

## Version History

| Version | Date | Milestone |
|---------|------|-----------|
| v3.0 | 2026-07-27 | Feature-complete: factions, replay, data-driven, dynamic maps, audio |
| v2.1 | 2026-07-25 | Asset quality overhaul: AI-generated sprites, alpha fixes |
| v2.0 | 2026-07-24 | Visual upgrade: military-industrial art style |
| v1.0.0 | 2026-07-15 | Initial release: core RTS gameplay |

---

## License

MIT License