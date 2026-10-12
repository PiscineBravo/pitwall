# Pitwall

An F1-style championship system for **Assetto Corsa**, built around **SimHub**.

Complete race weekends (practice, knockout qualifying, sprint, race), tyre degradation with pit
windows, a race engineer that talks to you, race control with flags, **virtual safety car**,
**safety car** and a real **formation lap**, dynamic weather with a forecast, automatic penalties,
and driver and constructor standings that update themselves at the end of every session.

Assetto Corsa has none of this in single player. Pitwall adds it from the outside, without
touching the game's own code.

---

## The three parts

| Component | Where it runs | What it does |
|---|---|---|
| **SimHub plugin** (`User.Pitwall.dll`) | inside SimHub | Reads telemetry, runs race control, tyres and the engineer, exposes everything as SimHub properties for dashboards and wheel displays, stores results |
| **Pitwall Manager** (`Pitwall.Manager.exe`) | standalone desktop app | Championships, calendar, drivers, teams, circuits, regulations, standings, live pit wall |
| **In-game apps** (CSP Lua) | inside Assetto Corsa | Race control overlay, delta bar, radio — and, where the track allows it, **actually slows the AI down** under safety car |

The three talk to each other through files in `Documents\Pitwall`. No server, no account, no
network. Each one works even when the others are not running.

```
Documents\Pitwall\
  championships\<id>.json     one complete championship per file
  circuits.json               circuit library
  live\state.json             state published by the plugin (read by the Manager)
  live\race_control.txt       compact version read by the in-game app
  live\command.json           commands from the Manager to the plugin
  exports\                    standings exported as CSV
```

---

## What it does

- **Full weekends** — practice, qualifying, sprint, race, in one launch or one session at a time
- **Knockout qualifying** — real Q1 / Q2 / Q3 with eliminations, a drop zone you can watch, and a
  grid built from the back towards pole, exactly the way it is composed in reality
- **Formation lap** — reconnaissance lap at a reduced pace, cars brought back onto the grid and
  held there, five lights with a variable delay, and a genuine standing start
- **Safety car and VSC** — with a target delta you have to respect; stay under it long enough and
  the stewards give you five seconds, once per period, as they do
- **Tyres and strategy** — degradation, pit windows, the two-compound rule, AI pit stops planned
  per driver, tyre choices that follow the weather
- **One race at a time** — Content Manager only ever offers the next round, and a race that has
  been completed cannot be run again and overwritten. The Manager holds the championship; the
  game only runs what it is told to run.
- **Race engineer** — spoken, with the information that actually matters at that moment
- **Dynamic weather** with a forecast the engineer reads to you
- **Italian and English** throughout, including the engineer and the in-game apps

---

## Requirements

- **Assetto Corsa** with **Custom Shaders Patch** and **Content Manager**
- **SimHub**
- **.NET 8 SDK**, only if you want to build it yourself

Pitwall works with any car and any track. The sample championship it creates on first run is set
up for the VRC Formula Alpha, but nothing is tied to it: change the car in the Teams tab and
everything else keeps working.

---

## Installing

```powershell
.\build.ps1
```

The script builds everything and then:

1. copies the plugin into SimHub's folder — **close SimHub first**, and if the copy is refused,
   reopen PowerShell as administrator, because SimHub lives in `Program Files`;
2. copies the Lua apps into `assettocorsa\apps\lua\`;
3. publishes the Manager to `dist\Manager\Pitwall.Manager.exe`.

Then, in SimHub: **Settings → Plugins**, enable **Pitwall**, restart SimHub. In Content Manager:
**Settings → Custom Shaders Patch → Lua apps**, enable **Pitwall**.

### What Pitwall changes in your Assetto Corsa

This is worth knowing before you install anything, and all of it is in the source:

- **`surfaces.ini` of the circuits in your calendar** gets a `[_SCRIPTING_PHYSICS]` section.
  Without it CSP will not let a script touch the physics, and without that there is no real
  safety car and no AI that genuinely slows down — only an overlay that says it does. The
  original file is backed up next to it and the change is reversible from the Manager.
- **A weather controller** is installed so the forecast can actually drive the sky. The original
  is backed up.
- **Two CSP graphics settings are turned off** — real mirrors and windscreen reflections in
  exterior views. Both are expensive and neither is worth its cost on a formula car with a full
  grid. If you want them back, they are two lines in `smart_mirror.ini` and `windscreen_fx.ini`.

---

## First run

Open `Pitwall.Manager.exe`. If it finds no championship it creates one that is ready to race:
twenty drivers, ten teams, and the full calendar built from the tracks installed on your machine.
Three things to check straight away:

1. **Drivers** — tick `You` on the driver you are actually going to race as.
2. **Drivers → AC name** — must match the name in the Assetto Corsa entry list character for
   character. It is the key telemetry is matched on: if it does not match, that driver never
   appears in the results.
3. **Circuits → Reference lap (s)** — the lap time the safety car and VSC pace are derived from.
   A wrong value here makes the delta impossible to respect.

Then just race. The plugin recognises the circuit and the session on its own, matches them to the
calendar, and writes the classification into the championship when the session ends.

---

## Status

It works end to end: complete weekends have been run from the first practice lap to the chequered
flag. It is also still young, and built mostly against one person's hardware and content.

`docs/da-provare.md` is the honest list of what is written but not yet switched on, and what is
still open. Read it before you file a bug — it may already be there, with the reason.

The documentation in `docs/` is in Italian and covers the parts that are not obvious from the
code: how the safety car decides, how the strategy is planned, how the weather is scheduled, and
how a Content Manager preset is actually built.

---

## Contributing

Bug reports, ideas and pull requests are all welcome — especially from anyone able to test on
tracks, cars and hardware that have never seen this code.

Two things that help a lot:

- **Say what you measured, not what you think happened.** Most of the hard bugs in this project
  were found by writing a number into a log and watching it, not by reading the code.
- **The regression suite runs without the game**: `dotnet run --project tools/sim --regressioni`
  covers race control, the formation lap, qualifying and storage, and the Lua behaviour checks
  run against the real script under `tools/sim/*_lua_checks.py`. If you fix something, leave a
  check behind so it cannot come back.

---

## Licence

**GNU General Public License v3.0** — see [LICENSE](LICENSE).

You may use, study, change and share this. If you distribute a modified version, it has to stay
open too: publish your source under the same licence. That is the point — this was given to the
community, and it should stay with the community.
