# Contributing to Iron Curtain RTS

Thank you for your interest in contributing! This guide will help you get started.

## Development Setup

### Prerequisites
- Godot 4.7.1 (Mono/.NET edition)
- .NET 8.0 SDK
- Git

### Getting Started
1. Fork the repository
2. Clone your fork
3. Open the project in Godot 4.7.1 (Mono)
4. Build: dotnet build RTSGame.sln
5. Run tests: cd tests/RTSGame.Tests && dotnet test
6. Press F5 in Godot editor to run the game

## Code Conventions

- **Language**: C# 12 (.NET 8.0)
- **Naming**: PascalCase for public members, _camelCase for private fields
- **Documentation**: XML doc comments on public APIs
- **Partial classes**: Keep Godot node structure intact; use partial classes (Main.*.cs) for logic separation
- **No Godot _Process in tests**: Test pure logic only; use GameLog.SafeMode in test assemblies
- **Data-driven**: New unit/building/faction stats go in data/*.json, not hardcoded
- **Logging**: Use GameLog instead of GD.Print (supports SafeMode for tests)

## Pull Request Process

1. Create a feature branch: git checkout -b feature/your-feature
2. Make your changes with clear, atomic commits
3. Ensure all 161 unit tests pass: dotnet test
4. Ensure zero new compiler errors (warnings are acceptable but document them)
5. Submit a PR against main
6. CI will automatically build and test

## Reporting Issues

Use the GitHub issue templates:
- **Bug Report**: For crashes, incorrect behavior, or performance issues
- **Feature Request**: For new gameplay features, quality-of-life improvements

Please include your OS, Godot version, and game commit hash.

## Project Structure

RTS_Game/
  scripts/      # C# source code (game logic) -- 58 files, ~28000 lines
  data/         # JSON data files (units, buildings, factions, techs, etc.)
  scenes/       # Godot .tscn scene files
  assets/       # Sprites, sounds, textures
  tests/        # xUnit test project (161 tests)
  .github/      # CI, issue/PR templates

## Architecture Notes

- **2D is the primary renderer** (isometric, classic RTS style)
- **3D exists as a technical prototype** (limited features, not production-ready)
- **Data-driven design**: Unit/building/faction stats in data/*.json with ModLoader support
- **Save system**: JSON with save_version field for forward compatibility
- **Replay system**: Records player commands only; AI is deterministic from seed + difficulty
- **Faction system**: 3 factions with whitelist gating + exclusive techs