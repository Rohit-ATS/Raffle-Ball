# Rift Rally

A hand-tracked VR table game for Meta Quest. You sit at a glowing rink the size of a coffee
table, pinch a stone, pull it back like a slingshot and let go. Closest stone to the centre
takes the round — and from round two onwards the rink itself starts fighting back.

Built for the MetaVR hackathon with Unity 6 (URP), OpenXR and XR Hands. No controllers: the
whole game is pinch, pull, release.

---

## How it plays

| | |
|---|---|
| **Throw** | Pinch the docked stone, pull back along the ice, release. Pull further for more power; pull off-centre for spin. |
| **Score** | Curling's rule: only the side with the closest stone scores, and it scores one point for every stone of its own that is closer than the opponent's best. |
| **Match** | Best of three rounds, three stones each per round. Losing a round earns the hammer (last stone) in the next one. |
| **Twists** | Round one is a clean rink. Every round after that reveals a modifier that changes where a smart player aims. |

### The modifiers

- **Cross Current** — a band across the middle pushes sliding stones sideways. Aim upstream, or throw harder to spend less time in the flow.
- **Bumper Posts** — three posts close the straight line to the house. Thread a gap or play a deliberate bank shot.
- **Warp Gates** — slide into one gate and come out of the other, along the exit gate's axis at the same speed. Predictable on purpose, so it is a tool rather than a dice roll.

The design rule for every modifier is that it must change where a good player aims. A twist
that only changes how the rink looks would make rounds feel varied while playing identically.

---

## Running it

### In the Editor

1. Open the project in **Unity 6000.6.9f1**.
2. **Rift Rally → Build Play Scene** generates `Assets/RiftRally/Scenes/RiftRally.unity` and
   puts it first in the build settings.
3. Press Play. With no headset attached the game falls back to a desktop camera and drives the
   right hand with the mouse — click to pinch — so the whole loop is playable at a desk.

### On a Quest

```
Unity.exe -batchmode -quit -projectPath . \
  -executeMethod RiftRally.EditorTools.RiftRallyBuild.Quest
```

Produces `Build/Quest/RiftRally.apk` (ARM64, IL2CPP, Vulkan, linear colour). Install with
`adb install -r Build/Quest/RiftRally.apk`. Hand tracking must be enabled on the headset.

> **The project path must be pure ASCII for Android builds.** Android's toolchain refuses any
> path containing non-ASCII characters, which includes a localized OneDrive `Documents` folder
> such as `ドキュメント`. The cheapest fix is an ASCII junction to the same files, which costs
> no disk space:
>
> ```
> mklink /J C:\RiftRallyBuild "<project folder>"
> Unity.exe -batchmode -quit -projectPath C:\RiftRallyBuild >   -executeMethod RiftRally.EditorTools.RiftRallyBuild.Quest
> ```
>
> Editor work, the desktop build and both test suites are unaffected.

A desktop build for quick checks:

```
Unity.exe -batchmode -quit -projectPath . \
  -executeMethod RiftRally.EditorTools.RiftRallyBuild.Windows
```

### Tests

```
Unity.exe -runTests -batchmode -projectPath . -testPlatform EditMode  -testResults EditMode.xml
Unity.exe -runTests -batchmode -projectPath . -testPlatform PlayMode  -testResults PlayMode.xml
```

EditMode covers the rink geometry and the tuning invariants. PlayMode runs real physics
frames: stones must stop where the analytic model says they will (the aim preview and the AI
both depend on that), and an AI-versus-AI match is played end to end at eight times speed to
prove the match loop cannot stall.

---

## How the code is arranged

```
Assets/RiftRally/
  Scripts/
    Core/         GameConfig, HandInput, MatchManager, ScoreKeeper
    Gameplay/     Rink, Stone, StoneLauncher, AimPreview, AiOpponent
    Modifiers/    RinkModifier + Current, Bumpers, WarpGates, ModifierDirector
    Presentation/ MatchHud, TextPanel, AudioDirector
    Setup/        GameBootstrap, RinkBuilder, ProceduralArt
  Editor/         Scene generator, command-line build entry points
  Tests/          EditMode and PlayMode suites
```

A handful of decisions are worth knowing before changing anything:

**The throw is a slingshot, not a flick.** Hand-tracking velocity is noisy, and one mis-read
spike would fire a stone off the rink. A pull-back offset is a stable quantity the player can
see and correct before releasing.

**Stones slide in a constrained 2.5D mode.** Gravity is off, Y is frozen and tilt is locked,
so stones behave like discs on a plane. Shots stay repeatable and readable, and the jitter and
tunnelling that plague thin rolling colliders at VR frame rates disappear.

**Friction is constant deceleration, not linear damping.** The stone holds its speed early and
then dies off predictably, which reads as ice. The same equation appears in exactly three
places — the stone, the aim preview and the AI's shot solver — and the PlayMode tests check
they still agree.

**The playfield is generated in code.** `RinkBuilder` builds the table, rings, walls, stones
and modifier props from `GameConfig`, so retuning a dimension cannot leave stale geometry
behind and the repository carries no binary art. The scene holds only the XR rig and one
`GameBootstrap`.

**Audio is synthesised at load.** Impacts are pitched and gained by the real collision impulse,
so a nudge and a hard takeout do not sound the same — the clearest cue the player has that a
shot landed.

**The HUD only listens.** It subscribes to `MatchManager` events and never calls back into it,
which is why the match loop can be driven headlessly by a test or by the attract demo with no
UI attached.

---

## Tuning

Nearly every number that affects feel lives in
[GameConfig.cs](Assets/RiftRally/Scripts/Core/GameConfig.cs) — rink size, stone mass, ice
friction, pinch thresholds, pull distance, round count, timings and team colours. The EditMode
tests guard the relationships between them that are easy to break by hand: the pinch release
gap must stay wider than the grab gap, friction must be able to cross the settle threshold
inside the hold window, a full-power throw must reach the back of the rink and a minimum-power
throw must fall short.
