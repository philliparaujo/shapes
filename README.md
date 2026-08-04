# Shapes

A 2-player, turn-based card game. See [PLAN.md](PLAN.md) for the full design and phase plan.

## Prerequisites

- **.NET 8 SDK (x64)** — verify with `dotnet --list-sdks`, which should report an
  `8.x` SDK under `C:\Program Files\dotnet\sdk`.

> **PATH note.** This machine also carries a 32-bit, runtime-only .NET at
> `C:\Program Files (x86)\dotnet`, which the *machine* PATH lists ahead of the x64 install.
> `C:\Program Files\dotnet` has been prepended to the *user* PATH (searched first) so a bare
> `dotnet` resolves to the SDK. If `dotnet --list-sdks` ever comes back empty, that entry has
> been lost — re-add it, or call the x64 binary directly:
>
> ```powershell
> & "$env:ProgramFiles\dotnet\dotnet.exe" build Shapes.sln
> ```

## Build and test

```powershell
dotnet build Shapes.sln
dotnet test Shapes.sln
dotnet run --project Shapes.Console
```

## Project layout

| Project           | Purpose                                                              |
|-------------------|----------------------------------------------------------------------|
| `Shapes.Core`     | The engine. Pure game logic — no UI, no I/O, no third-party deps.     |
| `Shapes.Content`  | Card definitions and rulesets as JSON. Data only, no code.           |
| `Shapes.Ai`       | IS-MCTS search, determinization, agents.                             |
| `Shapes.Console`  | Text client: human v human, human v AI, AI v AI.                     |
| `Shapes.Sim`      | Headless batch runner producing balance statistics.                  |
| `Shapes.Tests`    | xUnit suites: architecture, mechanics, effects, invariants.          |

`Shapes.Core` sits at the bottom of the dependency graph and everything points inward at it.
Keeping it free of UI and third-party dependencies is what makes the Phase 4 Godot migration a
client swap rather than a rewrite, and it is enforced by
`Shapes.Tests/Architecture/CorePurityTests.cs` rather than left to convention.

Those tests read `Shapes.Core.csproj` directly rather than inspecting the compiled assembly:
the C# compiler omits references that no code actually uses, so a dependency that is declared
but not yet used is invisible at runtime. Checking the project file catches it immediately.

## Conventions

- Shared build settings live in `Directory.Build.props`, not in individual `.csproj` files.
- Warnings are errors. `Nullable` is enabled everywhere.
- `Shapes.Core` builds with the trim/AOT analyzers on, because the Phase 4 iOS export requires
  AOT compilation. Prefer source-generated or explicit JSON handling over reflection.
