---
AIGC:
  ContentProducer: '001191110102MAD55U9H0F10002'
  ContentPropagator: '001191110102MAD55U9H0F10002'
  Label: '1'
  ProduceID: 'bc78f52a-3b89-4c3f-a5af-f244700564c0'
  PropagateID: 'bc78f52a-3b89-4c3f-a5af-f244700564c0'
  ReservedCode1: 'dd7c97a7-1dfd-4bad-b16e-9320a634a6d2'
  ReservedCode2: 'dd7c97a7-1dfd-4bad-b16e-9320a634a6d2'
---

# Changelog

All notable changes to Iron Curtain (RTS_Game) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- P2-5: GitHub Actions CI pipeline (auto build + test + coverage)
- P2-5: Issue/PR templates and CONTRIBUTING.md
- P2-5: Roslyn Analyzer (NetAnalyzers + Roslynator) for static code analysis

## [2.1.0] - 2026-07-22

### Added
- P1-5: Unified 2D/3D rendering pipeline with IRenderable/IUnitEntity/IBuildingEntity interfaces
- P1-5: GameData constants (TeamPalette, RenderLayer) centralized
- P1-5: UnitData snapshot structure for pure data extraction
- P1-5: BattleEffect ZIndex bug fix (was hidden behind units)

## [2.0.0] - 2026-07-20

### Added
- P1-4: Unit animation system (4320 frames: 27 units x 3 actions x 8 directions)
- P1-4: UnitAnimation.cs with caching and state machine
- P1-3: Built-in MapEditor with terrain painting, resource/obstacle placement, JSON save/load
- P1-2: Data-driven faction system (factions.json, 2 differentiated factions)
- P1-2: Unit/building stats externalized to data/*.json
- P1-1: 103 xUnit unit tests with coverlet coverage (95.5% on pure logic classes)

### Changed
- v2.0 visual upgrade: military-industrial art style for all units and buildings

## [1.0.0] - 2026-07-15

### Added
- P0-3: Main.cs god class split (5369 lines -> 8 files, -80.2%)
- P0-2: Save/Load system with JSON serialization and save_version field
- P0-1: A* pathfinding replacing direct-line movement
- Core RTS gameplay: build, train, combat, resource gathering
- 2D isometric and 3D renderers
- 8 faction support
- Strategic point capture system
- Difficulty selection (4 tiers)
- Minimap