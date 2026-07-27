# Changelog

All notable changes to Iron Curtain RTS will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.0.0] - 2026-07-27

### Added
- P1-2: Faction differentiation -- 3 factions (Allies/Soviet/Yuri) with exclusive units, buildings, techs
- P1-2: Faction selection UI in main menu
- P1-2: Faction whitelist system (CanProduceUnit/TryBuildBuilding gate by faction)
- P1-2: 6 faction-exclusive techs in TechTree (2 per faction)
- P3-1: Replay system -- ReplayRecorder/ReplayPlayer for recording and playing back full games
- P3-2: 2D/3D superweapon constant deduplication into GameConst
- GameLog.SafeMode for non-Godot runtime (fixes xUnit AccessViolation)
- ReplayRecorder.SetSilent() for test isolation
- ResourceLoader.Exists checks before GD.Load (eliminates ERROR spam)
- Version bumped to 3.0.0 (project.godot, export_presets.cfg, ReplayRecorder)

### Changed
- GameData.GetTeamColor now prioritizes FactionManager faction colors over TeamPalette
- TechOrder expanded from 12 to 18 (6 faction-exclusive additions)
- Building3D.GetProductionTime delegated to GameData (data-driven)

## [2.1.0] - 2026-07-25

### Added
- AI-generated building sprites (isometric + icons)
- AI-generated unit static sprites (gray hull + Modulate tinting)
- Alpha transparency batch fix (299 files, 253458 pixels)
- units_iso v2 generator (sharper, better saturation)

## [2.0.0] - 2026-07-24

### Added
- P2-5: GitHub Actions CI pipeline
- P2-5: Issue/PR templates, CONTRIBUTING.md
- P2-5: Roslyn Analyzer (NetAnalyzers + Roslynator)
- P2-1: Code cleanup -- GD.Print to GameLog, debug print removal
- P2-2: Dynamic map sizes (32/64/96) + 5 terrain themes
- P2-2: MapConfig with SetSize/SetTheme, game session integration
- P2-3: BGM manager (5 scenes) + unit voice system (10 units x 3 voices)
- P2-4: Data-driven architecture -- 9 JSON data files + ModLoader framework
- P2-4: TerrainGrid speed modifiers externalized
- P1-5: Unified 2D/3D rendering pipeline (IRenderable/IUnitEntity/IBuildingEntity)
- P1-5: GameData constants centralized (TeamPalette, RenderLayer)
- P1-5: UnitData snapshot structure
- P1-5: BattleEffect ZIndex bug fix

### Changed
- Military-industrial art style for all units and buildings
- MapSize hardcoded elimination -- MapConfig replaces const GridSize=32

## [1.0.0] - 2026-07-15

### Added
- P0-3: Main.cs god class split (5944 lines to 8 partial files, -77%)
- P0-2: Save/Load system with JSON serialization, save_version field
- P0-1: A* pathfinding replacing direct-line movement
- P0-4/P1-1: 161 xUnit unit tests (95.5% coverage on pure logic classes)
- P1-4: Unit animation system (4320 frames: 27 units x 3 actions x 8 dirs)
- P1-3: Built-in MapEditor (brush tools, resource placement, .rmap save/load)
- Core RTS gameplay: build, train, combat, resource gathering
- 2D isometric and 3D renderers
- 8 faction support with 4 difficulty tiers
- Strategic point capture system
- Minimap