using System;
using System.Collections;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Net;
using RiftRally.Presentation;
using RiftRally.Scenery;

namespace RiftRally.Setup
{
    /// <summary>
    /// The one component a scene needs. It builds the rink, the stones, the modifiers, the
    /// HUD and the audio, wires them together, and runs the attract loop.
    ///
    /// Having a single entry point keeps the scene file almost empty, which matters for two
    /// reasons: the XR rig can be swapped or re-imported without a scene merge, and an
    /// automated test can build the entire game in one call with no scene at all.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Where the table stands, relative to this object. Z is forward of the player.")]
        public Vector3 TableOffset = new Vector3(0f, 0f, 1.12f);

        [Tooltip("Yaw of the rink in degrees, so the table can be angled to the room.")]
        public float TableYaw = 0f;

        [Header("Match")]
        public GameMode Mode = GameMode.SoloVsAi;
        public AiSkill AiSkill = AiSkill.Sharp;

        [Tooltip("Skip the attract screen and throw the first stone straight away.")]
        public bool AutoStartMatch = false;

        [Tooltip("Run an AI versus AI demo match. Used by the attract loop and by tests.")]
        public bool DemoMatch = false;

        [Header("Scene services")]
        [Tooltip("Create a camera and a light if the scene has none. Off in a full XR scene.")]
        public bool CreateFallbackCameraAndLight = true;

        [Tooltip("Build the HUD signage.")]
        public bool CreateHud = true;

        [Tooltip("Build the procedural audio.")]
        public bool CreateAudio = true;

        [Header("Polish")]
        [Tooltip("Draw pinch rings and reach halos on the tracked hands.")]
        public bool CreateHandVisuals = true;

        [Tooltip("Slow time briefly on hard impacts and decisive shots.")]
        public bool EnableSlowMotion = true;

        [Tooltip("Apply the VR render quality preset and hold the frame rate budget.")]
        public bool ApplyQualitySettings = true;

        [Header("World")]
        [Tooltip("Build a landscape around the rink, rolling onto the next one each game.")]
        public bool CreateProceduralEnvironment = true;

        [Tooltip("Build the near-field HUD and the in-game options dock.")]
        public bool CreateHudDock = true;

        /// <summary>Owns the landscape, and swaps it between games.</summary>
        public LandscapeDirector Scenery { get; private set; }

        public HudDock Dock { get; private set; }

        public string EnvironmentName => Scenery != null ? Scenery.CurrentName : "none";

        [Tooltip("Simulate the headset and hands when no XR device is present, so the game " +
                 "can be played and tested on a desktop through the real VR code path.")]
        public bool SimulateVrOnDesktop = true;

        public DesktopVrSimulator Simulator { get; private set; }

        public QualityTuner.Preset Quality = QualityTuner.Preset.Balanced;

        [Header("Multiplayer")]
        [Tooltip("Build the network client, the online match driver and the lobby menu.")]
        public bool EnableMultiplayer = true;

        [Tooltip("Base URL of the multiplayer server.")]
        public string ServerUrl = "https://rift-rally-server.onrender.com";

        public RiftNetClient NetClient { get; private set; }
        public MainMenu Menu { get; private set; }

        // ---- everything the bootstrap owns, exposed for tests and for tooling ----
        public Rink Playfield { get; private set; }
        public StoneLauncher Launcher { get; private set; }
        public AiOpponent Ai { get; private set; }
        public MatchManager Match { get; private set; }
        public HandInput Hands { get; private set; }
        public MatchHud Hud { get; private set; }
        public Transform RinkRoot { get; private set; }

        private bool _built;

        private void Awake()
        {
            Build();
        }

        /// <summary>
        /// Constructs the game. Safe to call once; later calls are ignored. Public so an
        /// automated test can build the game without loading a scene.
        /// </summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            if (CreateFallbackCameraAndLight) EnsureCameraAndLight();
            ConfigurePhysics();

            // ---- rink ----
            var rootGo = new GameObject("RinkRoot");
            rootGo.transform.SetParent(transform, false);
            rootGo.transform.localPosition = TableOffset;
            rootGo.transform.localRotation = Quaternion.Euler(0f, TableYaw, 0f);
            RinkRoot = rootGo.transform;

            RinkBuilder.Built built = RinkBuilder.Build(RinkRoot);
            Playfield = built.Playfield;

            if (CreateProceduralEnvironment)
            {
                var sceneryGo = new GameObject("Scenery");
                sceneryGo.transform.SetParent(transform, false);

                Scenery = sceneryGo.AddComponent<LandscapeDirector>();
                Scenery.SetHost(sceneryGo.transform);

                // Density, terrain resolution and the texture budget, all before the first
                // build, so the opening landscape is baked once at the right resolution.
                Scenery.ConfigureQuality(Quality);

                // Launching is a new game, so it moves the rotation on. A player who put the
                // headset down in the desert comes back to somewhere else.
                Scenery.BuildForNewGame();
            }

            Debug.Log($"[RiftRally] Rink built; drawing with shader '{ProceduralArt.ShaderReport}', "
                      + $"text in '{Typography.FaceReport}'.");

            // ---- hands ----
            Hands = GetComponent<HandInput>();
            if (Hands == null) Hands = gameObject.AddComponent<HandInput>();
            Hands.TrackingSpace = FindTrackingSpace();
            Hands.DesktopPlaneHeight = Playfield.StoneCenterY;

            // With no headset attached, stand in for one. Added before anything reads hands
            // so the first frame already has them.
            if (SimulateVrOnDesktop && !UnityEngine.XR.XRSettings.isDeviceActive)
            {
                var simGo = new GameObject("DesktopVrSimulator");
                simGo.transform.SetParent(transform, false);
                Simulator = simGo.AddComponent<DesktopVrSimulator>();
                Simulator.Head = Camera.main != null ? Camera.main.transform : null;

                ReleaseCameraFromPoseDrivers(Simulator.Head);
            }

            // ---- throwing ----
            var launcherGo = new GameObject("StoneLauncher");
            launcherGo.transform.SetParent(transform, false);
            Launcher = launcherGo.AddComponent<StoneLauncher>();
            Launcher.Playfield = Playfield;
            Launcher.StonePrefab = built.StoneTemplate;
            Launcher.Preview = built.Preview;
            Launcher.EffectMaterial = built.Colors.Glow;

            // ---- opponent ----
            var aiGo = new GameObject("AiOpponent");
            aiGo.transform.SetParent(transform, false);
            Ai = aiGo.AddComponent<AiOpponent>();
            Ai.Playfield = Playfield;
            Ai.Launcher = Launcher;
            Ai.Modifiers = built.Director;
            Ai.Skill = AiSkill;

            // ---- match ----
            var matchGo = new GameObject("MatchManager");
            matchGo.transform.SetParent(transform, false);
            Match = matchGo.AddComponent<MatchManager>();
            Match.Playfield = Playfield;
            Match.Launcher = Launcher;
            Match.Modifiers = built.Director;
            Match.Ai = Ai;
            Match.Mode = DemoMatch ? GameMode.AiVsAi : Mode;
            Match.AutoStart = AutoStartMatch || DemoMatch;

            // ---- presentation ----
            if (CreateHud)
            {
                var hudGo = new GameObject("MatchHud");
                hudGo.transform.SetParent(transform, false);
                Hud = hudGo.AddComponent<MatchHud>();
                Hud.Match = Match;
                Hud.Playfield = Playfield;
            }

            // The near-field dock: score and options within reach, under the sightline to
            // the house. Built after the match so it can subscribe to it, and after the
            // scenery so its first frame is already in the biome palette.
            if (CreateHudDock)
            {
                var dockGo = new GameObject("HudDock");
                dockGo.transform.SetParent(transform, false);
                Dock = dockGo.AddComponent<HudDock>();
                Dock.Match = Match;
                Dock.Launcher = Launcher;
                Dock.Playfield = Playfield;
                Dock.Scenery = Scenery;
            }


            if (CreateHandVisuals)
            {
                var handsGo = new GameObject("HandVisuals");
                handsGo.transform.SetParent(transform, false);
                var visuals = handsGo.AddComponent<HandVisuals>();
                visuals.Hands = Hands;
                visuals.Launcher = Launcher;
            }

            // The house reacts to who is lying closest, which is how the scoring rule is
            // communicated without a single line of text.
            var glowGo = new GameObject("HouseGlow");
            glowGo.transform.SetParent(RinkRoot, false);
            var glow = glowGo.AddComponent<HouseGlow>();
            glow.Playfield = Playfield;
            glow.Rings = built.RingRenderers;

            // Round results and lead changes flash the house. Hooked to the match events
            // rather than driven from the HUD, so the effect fires even with no UI built.
            Match.RoundScored += _ => glow.Flash();
            Match.StealHappened += _ => glow.Flash();

            if (EnableSlowMotion) gameObject.AddComponent<SlowMotion>();

            if (ApplyQualitySettings)
            {
                var tuner = gameObject.AddComponent<QualityTuner>();
                tuner.Quality = Quality;
                tuner.Scenery = Scenery;
            }

            // Rolling the landscape over is hung off the end of a match rather than the
            // start of one. Building a world costs a few frames, and spending them while the
            // player is reading the final tally is free, where spending them as the first
            // stone is about to be thrown would not be.
            if (Scenery != null && Match != null) Match.MatchEnded += _ => RollLandscapeSoon();

            if (EnableMultiplayer)
            {
                var netGo = new GameObject("Net");
                netGo.transform.SetParent(transform, false);

                NetClient = netGo.AddComponent<RiftNetClient>();
                NetClient.ServerUrl = ServerUrl;

                var driver = netGo.AddComponent<NetMatchDriver>();
                driver.Net = NetClient;
                driver.Match = Match;
                driver.Launcher = Launcher;
                driver.Playfield = Playfield;
                driver.Modifiers = built.Director;

                // The lobby is only built when multiplayer is on, because every one of its
                // screens other than the solo button needs a server to talk to.
                var menuGo = new GameObject("MainMenu");
                menuGo.transform.SetParent(transform, false);
                Menu = menuGo.AddComponent<MainMenu>();
                Menu.Net = NetClient;
                Menu.Match = Match;
                Menu.Playfield = Playfield;
            }

            if (CreateAudio)
            {
                var audioGo = new GameObject("AudioDirector");
                audioGo.transform.SetParent(transform, false);
                var audio = audioGo.AddComponent<AudioDirector>();
                audio.Match = Match;
                audio.Launcher = Launcher;
                audio.Playfield = Playfield;
            }
        }

        /// <summary>
        /// Moves the rotation on to the next landscape a moment after a match finishes, so
        /// the lobby the player returns to is already somewhere new.
        /// </summary>
        private void RollLandscapeSoon()
        {
            if (!gameObject.activeInHierarchy) return;
            StartCoroutine(RollLandscape());
        }

        private IEnumerator RollLandscape()
        {
            // Real time, not scaled: a decisive final shot leaves the slow-motion effect
            // running, and the wait would stretch with it.
            yield return new WaitForSecondsRealtime(3.5f);
            if (Scenery != null) Scenery.BuildForNewGame();
        }

        /// <summary>
        /// Switches off anything driving the camera from a tracked device.
        ///
        /// The XR rig's pose driver keeps writing the camera transform even with no headset
        /// connected, which would pin the view at the origin and silently undo every move the
        /// simulator makes. Components are matched by type name rather than by type so this
        /// keeps working across XR package versions, and so the game does not take a
        /// compile-time dependency on a package it only needs when a rig happens to be present.
        /// </summary>
        private static void ReleaseCameraFromPoseDrivers(Transform head)
        {
            if (head == null) return;

            foreach (var behaviour in head.GetComponents<Behaviour>())
            {
                if (behaviour == null) continue;

                string name = behaviour.GetType().Name;
                if (name.Contains("TrackedPoseDriver") || name.Contains("CameraOffset"))
                {
                    behaviour.enabled = false;
                    Debug.Log($"[RiftRally] Desktop simulator disabled {name} on the camera.");
                }
            }
        }

        /// <summary>
        /// Physics is tuned for thin fast discs: a short fixed step and generous solver
        /// iterations, because a stone at full speed crosses its own radius in roughly one
        /// default step, which is exactly when collisions start being missed.
        /// </summary>
        private static void ConfigurePhysics()
        {
            Time.fixedDeltaTime = 1f / 90f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.defaultContactOffset = 0.002f;
            Physics.sleepThreshold = 0.0005f;
        }

        private void EnsureCameraAndLight()
        {
            if (Camera.main == null && Camera.allCamerasCount == 0)
            {
                var camGo = new GameObject("FallbackCamera") { tag = "MainCamera" };
                camGo.transform.SetParent(transform, false);
                camGo.transform.localPosition = new Vector3(0f, 1.35f, 0f);
                camGo.transform.localRotation = Quaternion.Euler(32f, 0f, 0f);

                var cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.02f;

                // Far enough to contain the landscape and the sky dome standing behind it.
                // This was 50 m, which is correct for a game that is only a table and was
                // quietly clipping away every hill and the entire sky.
                cam.farClipPlane = 400f;
                cam.fieldOfView = 62f;
                cam.clearFlags = CameraClearFlags.SolidColor;
            }

            // Whatever camera we ended up with - ours or the XR rig's - has to be able to see
            // both ends of the scale this game works at: a stone two centimetres across in
            // the hands, and a ridge sixty metres out. The landscape builder sets the
            // background to the biome horizon once it knows which biome it is.
            if (Camera.main != null)
            {
                Camera main = Camera.main;
                main.clearFlags = CameraClearFlags.SolidColor;
                main.nearClipPlane = Mathf.Min(main.nearClipPlane, 0.02f);
                main.farClipPlane = Mathf.Max(main.farClipPlane, 400f);
            }

            bool haveLight = false;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) { haveLight = true; break; }
            }

            if (!haveLight)
            {
                var lightGo = new GameObject("KeyLight");
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.localRotation = Quaternion.Euler(48f, 28f, 0f);

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.05f;
                light.color = new Color(0.92f, 0.95f, 1f);
                light.shadows = LightShadows.Soft;
            }
        }

        /// <summary>
        /// Hand joint poses arrive in the tracking origin's space, so the rig root has to be
        /// found to place hands in the world. The camera's root is used as the fallback, which
        /// is correct for every standard XR rig layout and harmless without one.
        /// </summary>
        private Transform FindTrackingSpace()
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name.StartsWith("XR Origin", System.StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            return Camera.main != null ? Camera.main.transform.root : transform;
        }

        // ---------------------------------------------------------------- attract loop

        private void Update()
        {
            if (Match == null || DemoMatch) return;

            // On a desktop, Enter starts a solo match straight away. This exists so the game
            // can be exercised without working through the lobby - useful when checking a
            // build, and the only way to drive it from an automated launch.
            if (Simulator != null && DesktopInput.PressedThisFrame(SimKey.Confirm))
            {
                bool idle = Match.State == MatchState.Attract
                            || Match.State == MatchState.MatchOver;
                if (idle)
                {
                    if (Menu != null) Menu.Hide();
                    Match.StartMatch(GameMode.SoloVsAi);
                    return;
                }
            }

            // With the lobby built, starting a match is the menu's job: a stray pinch while
            // reading the leaderboard must not drop the player into a solo game.
            if (Menu != null) return;

            bool waiting = Match.State == MatchState.Attract || Match.State == MatchState.MatchOver;
            if (!waiting) return;

            if (PlayerWantsToStart()) Match.StartMatch(Mode);
        }

        /// <summary>
        /// A pinch starts the match in a headset; a click or the space bar does the same on a
        /// desktop, so the game can be exercised without putting a headset on.
        /// </summary>
        private bool PlayerWantsToStart()
        {
            if (Hands != null)
            {
                if (Hands.Left.IsTracked && Hands.Left.IsPinching) return true;
                if (Hands.Right.IsTracked && Hands.Right.IsPinching) return true;
            }

            return PointerInput.SpacePressedThisFrame || PointerInput.PrimaryPressedThisFrame;
        }
    }
}
