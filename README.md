# Shapes

A 2-player, turn-based, board-and-cards game: three resources in a rock-paper-scissors cycle,
three board slots per side, creatures that merge. A C# engine with a console client, an IS-MCTS
AI, a headless balance simulator, and a Godot client for desktop and Android.

See [DESIGN.md](DESIGN.md) for the ruleset, the design decisions behind it, and the development
record.

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

## Common commands

Run from repo root (`shapes/`, where `Shapes.sln` lives).

| What                          | Command                                              |
|-------------------------------|-------------------------------------------------------|
| Build everything              | `dotnet build`                                       |
| Run all tests                 | `dotnet test Shapes.Tests/Shapes.Tests.csproj`       |
| Run one test by name          | `dotnet test Shapes.Tests/Shapes.Tests.csproj --filter "FullyQualifiedName~TestMethodName"` |
| Play the game (console)       | `dotnet run --project Shapes.Console`                |
| Play against the AI           | `dotnet run --project Shapes.Console -- --p2 greedy` |
| **Watch a full AI game**      | `dotnet run --project Shapes.Console -- --p1 greedy --p2 random --seed 7 --quiet` |
| Watch the search play         | `dotnet run --project Shapes.Console -- --p1 ismcts --p2 greedy --seed 7 --quiet` |
| **Run the agent matrix**      | `dotnet run -c Release --project Shapes.Sim -- --agents random,greedy,ismcts,ismcts-heuristic --games 30` |
| **See stats from played games** | `dotnet run -c Release --project Shapes.Sim -- --agents greedy,ismcts --games 30 --metrics-json out/metrics.json` |
| **Browse stats in the metrics explorer** | `dotnet run -c Release --project Shapes.Sim -- --agents greedy,ismcts --games 30 --report out/report.html` |
| **Re-explore a saved metrics.json** | `dotnet run -c Release --project Shapes.Sim -- --from-metrics-json out/metrics.json --report out/report.html` |
| **Compare two saved metrics.json runs** | `dotnet run -c Release --project Shapes.Sim -- --compare baseline/metrics.json,candidate/metrics.json --compare-report out/compare.html` |
| Run the relay locally           | `dotnet run --project Shapes.Relay -- --port 5080` |
| SSH into the relay VM          | `ssh -i .secrets/ssh-key-2026-08-18.key ubuntu@192.9.143.181` |
| Check relay service status/logs | `sudo systemctl status shapes-relay` / `sudo journalctl -u shapes-relay -f` |
| Restart the relay service       | `sudo systemctl restart shapes-relay` |
| Redeploy the relay after a code change | `dotnet publish Shapes.Relay/Shapes.Relay.csproj -c Release -r linux-x64 --self-contained false -o publish/relay-linux` then `scp -i .secrets/ssh-key-2026-08-18.key -r publish/relay-linux/. ubuntu@192.9.143.181:~/shapes-relay/` then `ssh -i .secrets/ssh-key-2026-08-18.key ubuntu@192.9.143.181 "sudo systemctl restart shapes-relay"` |

### Console options

`--p1`/`--p2` each take `human` (default), `random`, `greedy`, `ismcts`, or `ismcts-heuristic`
(a heuristic playout policy, same search otherwise). `--iterations <n>` sets the `ismcts`/
`ismcts-heuristic` search budget (default 200, in iterations so seeded games replay exactly).
`--seed <n>` skips the prompt; `--quiet` gives one line per action; `--reveal` shows both hands.
`--help` lists it all.

The waiting seat's hand renders as a count, so a human never reads the AI's cards.

### Simulator options

`Shapes.Sim` is where games are run in bulk and measured — the console client only renders a game
live and has no stats output of its own. Every run prints a metrics summary after the pairing
table, and takes:

- `--agents a,b,...` — which agents to run as a round-robin matrix.
- `--games <n>` — games per pairing.
- `--deck default|custom|random` — deck source (see [DESIGN.md](DESIGN.md) for what each means).
- `--json PATH` — full per-game detail plus the metrics report.
- `--metrics-json PATH` — just the aggregated `MetricsReport`, stamped with `RunProvenance` so
  two reports can be diffed.
- `--report PATH` — standalone HTML metrics explorer.
- `--from-metrics-json PATH` — re-explore a saved report without replaying games.
- `--compare a.json,b.json --compare-report PATH` — one-shot A/B diff of two saved runs.

A big matrix redraws a `completed/total games  rate  elapsed` progress line in place as games
finish — only when stdout is a real terminal, so piping to a file or CI log stays clean.

**Reading the output is its own topic** — which metric to trust, how many games a claim needs,
and what the numbers cannot decide are covered in
[DESIGN.md](DESIGN.md#reading-the-metrics). The short version: read take rate before win rate,
and sweeps need hundreds of games per configuration, not tens.

## Project layout

| Project                | Purpose                                                         |
|------------------------|-----------------------------------------------------------------|
| `Shapes.Core`          | The engine. Pure game logic — no UI, no I/O, no third-party deps. |
| `Shapes.Content`       | Card definitions and rulesets as JSON. Data only, no code.       |
| `Shapes.Ai`            | IS-MCTS search, determinization, agents.                         |
| `Shapes.Console`       | Text client: human v human, human v AI, AI v AI.                 |
| `Shapes.Sim`           | Headless batch runner producing balance statistics.              |
| `Shapes.Godot.Adapter` | View-model layer for the client: `GameSession`, `StateDiff`, text formatting. |
| `Shapes.Godot`         | Godot client (desktop + Android). Scenes and scripts only.       |
| `Shapes.Relay`         | Relay server two clients dial out to for networked play.         |
| `Shapes.Tests`         | xUnit suites: architecture, mechanics, effects, invariants.      |

`Shapes.Core` sits at the bottom of the dependency graph and everything points inward at it.
Keeping it free of UI and third-party dependencies is what made the Godot client a client swap
rather than a rewrite, and it is enforced by `Shapes.Tests/Architecture/CorePurityTests.cs`
rather than left to convention.

Those tests read `Shapes.Core.csproj` directly rather than inspecting the compiled assembly:
the C# compiler omits references that no code actually uses, so a dependency that is declared
but not yet used is invisible at runtime. Checking the project file catches it immediately.

`Shapes.Godot.Adapter` is deliberately separate from `Shapes.Godot`: the Godot.NET.Sdk source
generator needs a `GodotProjectDir` MSBuild property that only the Godot editor supplies, so
`Shapes.Tests` cannot reference `Shapes.Godot.csproj` outside the editor. The adapter is a plain
class library, so it builds and tests under an ordinary `dotnet build` like everything else.

## Conventions

- Shared build settings live in `Directory.Build.props`, not in individual `.csproj` files.
- Warnings are errors. `Nullable` is enabled everywhere.
- `Shapes.Core` builds with the trim/AOT analyzers on, because the mobile export requires AOT
  compilation. Prefer source-generated or explicit JSON handling over reflection.
- Cards and rules are JSON, not C# — a balance change never needs a recompile.
- All randomness goes through one seeded `IRandomSource`; any game is reproducible from its seed.

The design constraints behind these — and the rest of the cross-cutting principles — are in
[DESIGN.md](DESIGN.md#3-cross-cutting-principles).
