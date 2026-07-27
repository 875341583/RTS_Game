---
AIGC:
  ContentProducer: '001191110102MAD55U9H0F10002'
  ContentPropagator: '001191110102MAD55U9H0F10002'
  Label: '1'
  ProduceID: 'e31dd7e8-dcf8-4a12-a39b-60133fb6d10e'
  PropagateID: 'e31dd7e8-dcf8-4a12-a39b-60133fb6d10e'
  ReservedCode1: 'b4ed72ae-cf88-4fa2-b1ef-145c457c2c18'
  ReservedCode2: 'b4ed72ae-cf88-4fa2-b1ef-145c457c2c18'
---

# Contributing to Iron Curtain (RTS_Game)

Thank you for your interest in contributing! This guide will help you get started.

## Development Setup

### Prerequisites
- Godot 4.7.1 (Mono/.NET edition)
- .NET 8.0 SDK
- Git

### Getting Started
1. Fork the repository
2. Clone your fork: `git clone git@github.com:YOUR_USERNAME/RTS_Game.git`
3. Open the project in Godot 4.7.1 (Mono)
4. Build: `dotnet build RTSGame.csproj`
5. Run tests: `cd tests/RTSGame.Tests && dotnet test`

## Code Conventions

- **Language**: C# 12 (.NET 8.0)
- **Naming**: PascalCase for public members, _camelCase for private fields
- **Documentation**: XML doc comments on public APIs
- **Partial classes**: Keep Godot node structure intact; use partial classes for logic separation
- **No Godot _Process in tests**: Test pure logic only, mock Godot APIs

## Pull Request Process

1. Create a feature branch: `git checkout -b feature/your-feature`
2. Make your changes with clear, atomic commits
3. Ensure all 103+ unit tests pass: `dotnet test`
4. Ensure zero new compiler warnings
5. Submit a PR against `main`
6. CI will automatically build and test

## Reporting Issues

Use the GitHub issue templates:
- **Bug Report**: For crashes, incorrect behavior, or performance issues
- **Feature Request**: For new gameplay features, quality-of-life improvements

Please include your OS, Godot version, and game commit hash.

## Project Structure

```
RTS_Game/
  scripts/      # C# source code (game logic)
  data/         # JSON data files (units, buildings, factions)
  scenes/       # Godot .tscn scene files
  textures/     # Sprite assets
  assets/       # Additional assets
  tests/        # xUnit test project
```

## Architecture Notes

- **2D is the primary renderer** (isometric, Red Alert 2 style)
- **3D exists as a technical prototype** (limited features)
- **Data-driven design**: Unit/building stats in `data/*.json`
- **Save system**: JSON with `save_version` field for forward compatibility