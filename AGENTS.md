# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A jetpack shooter in the style of Ultimate Play the Game's 1983 Jetpac, in C# on .NET 10 and Avalonia 12, drawn
through Avalonia's `DrawingContext`. Build the rocket, fuel it pod by pod, and blast off to the next planet while lasering aliens. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code, and
the sounds, music and spoken countdown are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAJetpac.slnx                       # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAJetpac.slnx                        # every test runs headless: no window opens
dotnet run --project src/AVAJetpac                # play it; --no-audio for silence
JETPAC_SHOTS=$PWD/Images dotnet test tests/AVAJetpac.Tests --filter ScreenshotTests   # refresh the README's pictures
JETPAC_PROBE=1 dotnet test tests/AVAJetpac.Tests --filter PilotProbe --logger "console;verbosity=detailed"
                                                  # how the computer Jetman does (JETPAC_PROBE=lazy: fly about firing)
python3 tools/make_sounds.py                      # rebuild src/AVAJetpac/Sounds (standard-library Python only)
```

`tools/speech.py` is a small formant speech synthesiser that `make_sounds.py` imports for the voice.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAJetpac/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, initials, high scores), the phases of a level,
    the `Sfx` list and the fixed-step loop.
  - `World.cs` has the measures (`Field`: the screen, ledges and pad), the `Dice` generator, `Controls`, and
    the world's state: Jetman, aliens, items and the rocket.
  - `Sim.cs` steps the world: flying, the laser, carrying rocket sections and fuel, aliens, and starting levels.
  - `Pilot.cs` is the computer Jetman, used by the attract mode and the tests. It tries moves on copies of
    the world (`World.Copy()`), so new world state must be copied there too.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAJetpac/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024 x 768 picture scaled to the
  window and runs the 60 Hz timer. Ctrl+S saves a screenshot to the Pictures folder.
- `src/AVAJetpac/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAJetpac.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, screenshot, Shift-release tests and
  the pilot probe.

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`Field.Step`) from an accumulator, independent of
  the frame rate, so tests can play deterministically. Draw random numbers from the world's seeded `Dice`
  (`w.Dice`, seeded per level in `Sim`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** `Controls` has no up/down: up (key, D-pad or stick pushed to negative Y)
  means thrust. Test new controls through `InputHandler`, not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. Shift thrusts here, so this matters; `ShiftReleaseTests` covers it.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAJetpac`); tests load the
  table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change the rules or the pilot, run the probe above to see the computer Jetman still gets on.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release; either then asks the portfolio site to rebuild.
