using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Presentation;

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

            // ---- hands ----
            Hands = GetComponent<HandInput>();
            if (Hands == null) Hands = gameObject.AddComponent<HandInput>();
            Hands.TrackingSpace = FindTrackingSpace();
            Hands.DesktopPlaneHeight = Playfield.StoneCenterY;

            // ---- throwing ----
            var launcherGo = new GameObject("StoneLauncher");
            launcherGo.transform.SetParent(transform, false);
            Launcher = launcherGo.AddComponent<StoneLauncher>();
            Launcher.Playfield = Playfield;
            Launcher.StonePrefab = built.StoneTemplate;
            Launcher.Preview = built.Preview;

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
                cam.farClipPlane = 50f;
                cam.fieldOfView = 62f;
                cam.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
                cam.clearFlags = CameraClearFlags.SolidColor;
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
