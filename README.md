# Rift Rally

A hand-tracked VR table game for Meta Quest. You sit at a glowing rink the size of a coffee
table, pinch a stone, pull it back like a slingshot and let go. Closest stone to the centre
takes the round — and from round two onwards the rink itself starts fighting back.

Play solo against the AI, or 1v1 online — quick match a stranger, or read a four-character
room code out to a friend. Guests and registered accounts share one leaderboard.

Built for the MetaVR hackathon with Unity 6 (URP), OpenXR and XR Hands, with a Node
multiplayer service on Render. No controllers: the whole game is pinch, pull, release.

---

## How it plays

| | |
|---|---|
| **Throw** | Pinch the docked stone, pull back along the ice, release. Pull further for more power; pull off-centre for spin. |
| **Score** | Curling's rule: only the side with the closest stone scores, and it scores one point for every stone of its own that is closer than the opponent's best. |
| **Match** | Best of three rounds, three stones each per round. Losing a round earns the hammer (last stone) in the next one. |
| **Twists** | Round one is a clean rink. Every round after that reveals a modifier that changes where a smart player aims. |
| **Online** | 1v1 by room code or quick match. Leaderboard ranks guests and accounts together; a guest who signs up keeps the rating they already earned. |

### The modifiers

- **Cross Current** — a band across the middle pushes sliding stones sideways. Aim upstream, or throw harder to spend less time in the flow.
- **Bumper Posts** — three posts close the straight line to the house. Thread a gap or play a deliberate bank shot.
- **Warp Gates** — slide into one gate and come out of the other, along the exit gate's axis at the same speed. Predictable on purpose, so it is a tool rather than a dice roll.

The design rule for every modifier is that it must change where a good player aims. A twist
that only changes how the rink looks would make rounds feel varied while playing identically.

---

## Download size, and what "4K" means here

The Quest build is **46 MB**. There is nothing to compress: the rink, the stones, the rings,
the hands, the HUD and every effect are generated in code at runtime, and all seven sounds are
synthesised at load. The repository carries no textures, no meshes and no audio files, so the
APK is essentially just the Unity runtime.

On resolution, the honest version: a headset has a fixed panel, so a game cannot output 4K to
it. What it controls is the size of the eye buffer rendered *before* that buffer is sampled
onto the panel. Rendering above panel resolution and downsampling — supersampling — is what
removes shimmer from thin bright geometry, and this game is almost entirely thin bright
geometry: ring outlines, aim lines, trails. So that is the dial that matters here, and
[`QualityTuner.cs`](Assets/RiftRally/Scripts/Setup/QualityTuner.cs) sets it:

| Preset | Eye texture scale | MSAA | For |
|---|---|---|---|
| Crisp | 1.4× | 4× | Quest 3 and up |
| Balanced *(default)* | 1.2× | 4× | Quest 2, comfortable headroom |
| Fast | 1.0× | 2× | fallback when frames are being missed |

MSAA rather than post-process antialiasing, because on a tiled mobile GPU it is nearly free
and, unlike FXAA or TAA, it does not blur — and a smeared aim line in a throwing game is lost
information. Quality steps itself down automatically if the frame budget is missed for four
seconds straight; holding the refresh rate matters more than any of this, because dropping
frames in VR is not "choppy", it is nauseating.

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

### Multiplayer

The server lives in [`server/`](server/) and deploys to Render from the
[`render.yaml`](render.yaml) blueprint — see [server/README.md](server/README.md) for the
deploy steps, the API and the protocol. Once deployed, set the service URL on the
`GameBootstrap` component's **Server Url** field and rebuild.

### Tests

```
Unity.exe -runTests -batchmode -projectPath . -testPlatform EditMode  -testResults EditMode.xml
Unity.exe -runTests -batchmode -projectPath . -testPlatform PlayMode  -testResults PlayMode.xml
cd server && npm test
```

EditMode covers the rink geometry, the tuning invariants, the hand-filter behaviour and the
wire format. PlayMode runs real physics frames: stones must stop where the analytic model says
they will (the aim preview and the AI both depend on that), and an AI-versus-AI match is played
end to end at eight times speed to prove the match loop cannot stall. The server suite plays a
complete match between two real WebSocket clients.

---

## How the code is arranged

```
Assets/RiftRally/
  Scripts/
    Core/         GameConfig, HandInput, OneEuroFilter, PointerInput,
                  MatchManager, ScoreKeeper
    Gameplay/     Rink, Stone, StoneLauncher, AimPreview, AiOpponent
    Modifiers/    RinkModifier + Current, Bumpers, WarpGates, ModifierDirector
    Presentation/ MatchHud, MainMenu, PinchButton, HandVisuals, StoneVfx,
                  HouseGlow, SlowMotion, Easing, TextPanel, AudioDirector
    Net/          RiftNetClient, NetMatchDriver, NetProtocol
    Setup/        GameBootstrap, RinkBuilder, ProceduralArt, QualityTuner
  Editor/         Scene generator, command-line build entry points
  Tests/          EditMode and PlayMode suites
server/           Node multiplayer service (see server/README.md)
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
UI attached — and why an online match, driven entirely by the server through
`MatchManager.External*`, lights up the same HUD, audio and house glow without any of them
knowing they are online.

**Hand positions are one-euro filtered.** Raw tracked joints jitter by millimetres every
frame, and a plain low-pass filter trades that jitter for lag, which on a throw is worse. A
one-euro filter smooths hard while the hand is still and barely at all while it moves fast, so
a held stone sits rock steady and a quick pull still tracks the hand. Its beta is unit
dependent and that is the trap — the literature's value assumes pixels, and using it on metres
left 86 mm of lag on a normal pull. There is a test for exactly that.

**Nothing in VR moves the camera.** Slow motion scales time, never the view; impacts flash
lights and emission, never the head. Camera shake and view-space effects are the fastest route
to motion sickness, so the drama is all in the world.

**Menus are physical.** The lobby is a board standing on the table that you press with your
finger and a pinch, not a head-locked panel with a laser pointer. Direct touch is steadier
with hand tracking, it reuses the one gesture the game already teaches, and the room keeps
feeling like a room.

**Your own throw is never networked while you make it.** Online, you throw against local
physics with zero latency and report the result; the opponent replays your launch and then
adopts the reported positions. The server owns turn order and scoring, but client-reported
physics cannot be independently verified, so these fast hackathon matches are retained in match
history without changing wins or Elo. The full reasoning is in
[server/README.md](server/README.md).

---

## Tuning

Nearly every number that affects feel lives in
[GameConfig.cs](Assets/RiftRally/Scripts/Core/GameConfig.cs) — rink size, stone mass, ice
friction, pinch thresholds, pull distance, round count, timings and team colours. The EditMode
tests guard the relationships between them that are easy to break by hand: the pinch release
gap must stay wider than the grab gap, friction must be able to cross the settle threshold
inside the hold window, a full-power throw must reach the back of the rink and a minimum-power
throw must fall short.
