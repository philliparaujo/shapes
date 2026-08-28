# Shapes

## Screenshots

Click here to see pictures of the game in action: TODO

## Description

Shapes is a two-player, turn-based card game built in C#. In Shapes, you battle with cards in a fictional universe full of spherical **wheel** creatures, blunt **anvil** creatures, and sharp **spike** creatures. You win by maintaining board control which whittles your opponent's health to zero.

The game is installable on Windows and Android with singleplayer, local multiplayer, or (private) online multiplayer options.

See [DESIGN.md](DESIGN.md) for the full ruleset, the engineering decisions behind it, and the development timeline the project was built against.

## Installation
To play Shapes on desktop, go to `Releases` and download the latest `.exe`. 
To play Shapes on mobile, go to `Releases` and download the latest `.apk`.

## Project Structure

The project is a .NET 8 solution — a set of separate C# projects, each building to its own library or executable, tied together by `Shapes.sln` so they build and test as one unit. Each is a directory at the repo root:

- `Shapes.Ai`: Agents, IS-MCTS search, determinization 
- `Shapes.Console`: Two-player game text client
- `Shapes.Content`: Card definitions and rulesets as JSON
- `Shapes.Core`: The engine as pure game logic (no UI, I/O, or third-party deps)
- `Shapes.Godot`: Godot client: scenes, scripts, assets, export config
- `Shapes.Godot.Adapter`: Client-side layer between the engine and the UI — `GameSession`, `StateDiff`, relay networking, saved decks/matches, and text formatting
- `Shapes.Relay`: Relay server that two clients dial out to for networked play
- `Shapes.Sim`: Headless batch runner producing balance statistics
- `Shapes.Tests`: xUnit suites — mechanics, effects, AI, and fuzz

## Development Setup

1. Install the [.NET 8 SDK (x64)](https://dotnet.microsoft.com/download/dotnet/8.0) — verify with `dotnet --list-sdks`, which should report an `8.x` SDK under `C:\Program Files\dotnet\sdk`

> **PATH note.** If `dotnet --list-sdks` comes back empty, a 32-bit runtime-only .NET at `C:\Program Files (x86)\dotnet` is shadowing the x64 SDK on PATH. Prepend `C:\Program Files\dotnet` to your user PATH, or call the x64 binary directly:
> ```powershell
> & "$env:ProgramFiles\dotnet\dotnet.exe" build Shapes.sln
> ```

2. Clone or download the repository onto your local machine
```
git clone https://github.com/philliparaujo/shapes.git
cd shapes
```
3. Build and test from the repo root, where `Shapes.sln` lives
```
dotnet build
dotnet test Shapes.Tests/Shapes.Tests.csproj
```
4. Install [Godot](https://godotengine.org/download) (.NET build) and open `Shapes.Godot/` in the editor to work on the game client

### Console options

Run the text client with `dotnet run --project Shapes.Console`:

```
dotnet run --project Shapes.Console                                                  # two humans
dotnet run --project Shapes.Console -- --p2 ismcts                                   # play the AI
dotnet run --project Shapes.Console -- --p1 ismcts --p2 greedy --seed 7 --quiet      # watch an AI game
```

- `--p1`/`--p2` each take `human` (default), `random`, `greedy`, `ismcts`, or `ismcts-heuristic` 
- `--iterations <n>` sets the search budget (default 200)
- `--seed <n>` skips the prompt
- `--quiet` gives one line per action
- `--reveal` shows both hands
- `--help` lists it all.

### Simulator options

`Shapes.Sim` is where games are run in bulk and measured — the console client only renders a game live and has no stats output of its own.

```
# round-robin matrix of every agent
dotnet run -c Release --project Shapes.Sim -- --agents random,greedy,ismcts,ismcts-heuristic --games 30

# same, plus a browsable HTML metrics explorer
dotnet run -c Release --project Shapes.Sim -- --agents greedy,ismcts --games 30 --report out/report.html

# re-explore a saved run without replaying games
dotnet run -c Release --project Shapes.Sim -- --from-metrics-json out/metrics.json --report out/report.html

# A/B diff two saved runs
dotnet run -c Release --project Shapes.Sim -- --compare baseline/metrics.json,candidate/metrics.json --compare-report out/compare.html
```

Every run prints a metrics summary after the pairing table, and takes:

- `--agents a,b,...`: which agents to run as a round-robin matrix
- `--games <n>`: games per pairing
- `--deck default|custom|random`: deck source
- `--json PATH`: full per-game detail plus the metrics report
- `--metrics-json PATH`: just the aggregated `MetricsReport`, stamped with `RunProvenance` so two reports can be diffed
- `--report PATH`: standalone HTML metrics explorer
- `--from-metrics-json PATH`: re-explore a saved report without replaying games
- `--compare a.json,b.json --compare-report PATH`: one-shot A/B diff of two saved runs

### Relay deployment

Run a relay locally with `dotnet run --project Shapes.Relay -- --port 5080`.

In production it runs as a `systemd` service on a VM. To redeploy after a code change:

```
dotnet publish Shapes.Relay/Shapes.Relay.csproj -c Release -r linux-x64 --self-contained false -o publish/relay-linux
scp -i .secrets/<ssh-key> -r publish/relay-linux/. ubuntu@<relay-host>:~/shapes-relay/
ssh -i .secrets/<ssh-key> ubuntu@<relay-host> "sudo systemctl restart shapes-relay"
```

Check status and logs over SSH with `sudo systemctl status shapes-relay` and `sudo journalctl -u shapes-relay -f`.
