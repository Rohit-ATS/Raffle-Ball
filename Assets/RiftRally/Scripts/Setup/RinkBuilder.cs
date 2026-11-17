using System.Collections.Generic;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Modifiers;

namespace RiftRally.Setup
{
    /// <summary>
    /// Builds the playfield, the stone template and the modifier props from
    /// <see cref="GameConfig"/>.
    ///
    /// Everything is parented under a single root so the finished rink can be dropped onto a
    /// real table by moving one transform, which is what the passthrough placement flow does.
    /// </summary>
    public static class RinkBuilder
    {
        public struct Palette
        {
            public Material Table;
            public Material Ice;
            public Material Wall;
            public Material RingOuter;
            public Material RingMiddle;
            public Material RingInner;
            public Material Marking;
            public Material Glow;
            public Material StoneBody;
            public Material PostBody;
            public Material CurrentBand;

            public static Palette Default()
            {
                return new Palette
                {
                    Table = ProceduralArt.MakeMaterial(new Color(0.09f, 0.10f, 0.13f), 0f, 0.25f),
                    Ice = ProceduralArt.MakeMaterial(new Color(0.11f, 0.15f, 0.22f), 0.15f, 0.90f),
                    Wall = ProceduralArt.MakeMaterial(new Color(0.20f, 0.24f, 0.32f), 0.10f, 0.55f),
                    RingOuter = ProceduralArt.MakeMaterial(new Color(0.20f, 0.42f, 0.55f), 1.1f),
                    RingMiddle = ProceduralArt.MakeMaterial(new Color(0.35f, 0.70f, 0.80f), 1.6f),
                    RingInner = ProceduralArt.MakeMaterial(GameConfig.TargetColor, 2.6f),
                    Marking = ProceduralArt.MakeMaterial(new Color(0.55f, 0.62f, 0.72f), 0.6f),
                    Glow = ProceduralArt.MakeMaterial(Color.white, 2.0f),
                    StoneBody = ProceduralArt.MakeMaterial(Color.white, 0.6f, 0.75f, 0.2f),
                    PostBody = ProceduralArt.MakeMaterial(new Color(0.95f, 0.78f, 0.25f), 1.3f),
                    CurrentBand = ProceduralArt.MakeMaterial(new Color(0.30f, 0.85f, 0.95f), 1.0f)
                };
            }
        }

        /// <summary>Everything the bootstrap needs to wire the game together.</summary>
        public class Built
        {
            public Rink Playfield;
            public Transform Surface;
            public GameObject StoneTemplate;
            public ModifierDirector Director;
            public AimPreview Preview;
            public Palette Colors;
        }

        /// <summary>
        /// Builds the rink under <paramref name="root"/>.
        /// </summary>
        /// <param name="root">Transform the rink is parented to. Its position is the floor
        /// under the table, so the surface ends up at <see cref="GameConfig.RinkHeight"/>.</param>
        public static Built Build(Transform root)
        {
            var palette = Palette.Default();

            float halfL = GameConfig.RinkLength * 0.5f;
            float halfW = GameConfig.RinkWidth * 0.5f;

            // ---- rink root and surface plane ----
            var rinkGo = new GameObject("Rink");
            rinkGo.transform.SetParent(root, false);
            rinkGo.transform.localPosition = Vector3.zero;

            var surface = new GameObject("Surface").transform;
            surface.SetParent(rinkGo.transform, false);
            surface.localPosition = new Vector3(0f, GameConfig.RinkHeight, 0f);
            surface.localRotation = Quaternion.identity;

            var rink = rinkGo.AddComponent<Rink>();
            rink.Surface = surface;

            // ---- table body, sitting under the playing surface ----
            const float topThickness = 0.03f;
            ProceduralArt.Box(surface, "TableTop",
                new Vector3(0f, -topThickness * 0.5f, 0f),
                new Vector3(GameConfig.RinkWidth + 0.09f, topThickness, GameConfig.RinkLength + 0.09f),
                palette.Table, true);

            ProceduralArt.Box(surface, "Ice",
                new Vector3(0f, -0.004f, 0f),
                new Vector3(GameConfig.RinkWidth, 0.008f, GameConfig.RinkLength),
                palette.Ice, false);

            float legHeight = GameConfig.RinkHeight - topThickness;
            float legInsetX = halfW - 0.05f;
            float legInsetZ = halfL - 0.05f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    ProceduralArt.Box(rinkGo.transform, $"Leg_{sx}_{sz}",
                        new Vector3(legInsetX * sx, legHeight * 0.5f, legInsetZ * sz),
                        new Vector3(0.035f, legHeight, 0.035f),
                        palette.Table, false);
                }
            }

            // ---- walls ----
            // Side and far walls keep stones in play; the near end is deliberately open so a
            // stone knocked backwards can fall off and be out, which is what makes guarding
            // the front of the house a real risk.
            float wallT = 0.012f;
            float wallY = GameConfig.WallHeight * 0.5f;

            ProceduralArt.Box(surface, "WallLeft",
                new Vector3(-halfW - wallT * 0.5f, wallY, 0f),
                new Vector3(wallT, GameConfig.WallHeight, GameConfig.RinkLength + wallT * 2f),
                palette.Wall, true);

            ProceduralArt.Box(surface, "WallRight",
                new Vector3(halfW + wallT * 0.5f, wallY, 0f),
                new Vector3(wallT, GameConfig.WallHeight, GameConfig.RinkLength + wallT * 2f),
                palette.Wall, true);

            ProceduralArt.Box(surface, "WallFar",
                new Vector3(0f, wallY, halfL + wallT * 0.5f),
                new Vector3(GameConfig.RinkWidth + wallT * 2f, GameConfig.WallHeight, wallT),
                palette.Wall, true);

            // ---- scoring target ----
            float targetZ = halfL - GameConfig.TargetFromFarEnd;
            var targetRoot = new GameObject("Target").transform;
            targetRoot.SetParent(surface, false);
            targetRoot.localPosition = new Vector3(0f, 0.0012f, targetZ);
            rink.TargetCenter = targetRoot;

            var radii = GameConfig.RingRadii;   // outermost first
            var ringMats = new[] { palette.RingOuter, palette.RingMiddle, palette.RingInner };

            for (int i = 0; i < radii.Length; i++)
            {
                float outer = radii[i];
                float inner = i + 1 < radii.Length ? radii[i + 1] : 0f;
                Material mat = ringMats[Mathf.Min(i, ringMats.Length - 1)];

                if (inner <= 0.0001f)
                {
                    ProceduralArt.Disc(targetRoot, $"Ring{i}", new Vector3(0f, i * 0.0004f, 0f),
                                       outer, mat);
                }
                else
                {
                    ProceduralArt.Ring(targetRoot, $"Ring{i}", new Vector3(0f, i * 0.0004f, 0f),
                                       inner, outer, mat);
                }
            }

            // Thin bright outlines make the ring edges readable at a glance in a headset.
            for (int i = 0; i < radii.Length; i++)
            {
                ProceduralArt.Ring(targetRoot, $"RingEdge{i}", new Vector3(0f, 0.0022f, 0f),
                                   radii[i] - 0.0016f, radii[i], palette.Glow);
            }

            // ---- launch pads ----
            foreach (PlayerSide side in new[] { PlayerSide.One, PlayerSide.Two })
            {
                Vector3 world = rink.LaunchPointWorld(side);
                Vector3 local = surface.InverseTransformPoint(world);
                var mat = ProceduralArt.MakeMaterial(side.ToColor(), 1.4f);

                ProceduralArt.Ring(surface, $"LaunchPad_{side}",
                    new Vector3(local.x, 0.0014f, local.z),
                    GameConfig.StoneRadius * 1.35f, GameConfig.StoneRadius * 1.6f, mat);
            }

            // Hog line marking the launch line, so the player can see where throws start.
            float launchZ = -halfL + GameConfig.LaunchLineFromNearEnd;
            ProceduralArt.Box(surface, "LaunchLine",
                new Vector3(0f, 0.0012f, launchZ),
                new Vector3(GameConfig.RinkWidth, 0.0008f, 0.004f),
                palette.Marking, false);

            // ---- stone template ----
            GameObject stoneTemplate = BuildStoneTemplate(root, palette);

            // ---- aim preview ----
            AimPreview preview = BuildAimPreview(rinkGo.transform, rink, palette);

            // ---- modifiers ----
            ModifierDirector director = BuildModifiers(rinkGo.transform, surface, palette);

            return new Built
            {
                Playfield = rink,
                Surface = surface,
                StoneTemplate = stoneTemplate,
                Director = director,
                Preview = preview,
                Colors = palette
            };
        }

        // ---------------------------------------------------------------- stone

        /// <summary>
        /// Builds the stone used as the instantiation source. It is kept inactive and parked
        /// under the root, so it neither renders nor simulates until a copy is thrown.
        /// </summary>
        public static GameObject BuildStoneTemplate(Transform root, Palette palette)
        {
            var go = new GameObject("StoneTemplate");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, -2f, 0f);

            var body = ProceduralArt.Cylinder(go.transform, "Body", Vector3.zero,
                GameConfig.StoneRadius, GameConfig.StoneHeight, palette.StoneBody, false);

            // A bright cap on top reads as the handle and shows the stone's team colour from
            // directly above, which is the angle the player actually looks from.
            ProceduralArt.Disc(body.transform.parent, "Cap",
                new Vector3(0f, GameConfig.StoneHeight * 0.5f + 0.0006f, 0f),
                GameConfig.StoneRadius * 0.55f, palette.Glow);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = GameConfig.StoneMass;

            var collider = go.AddComponent<SphereCollider>();
            collider.radius = GameConfig.StoneRadius;
            collider.material = new PhysicsMaterial("Stone")
            {
                dynamicFriction = 0.02f,
                staticFriction = 0.02f,
                bounciness = 0.35f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Maximum
            };

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.55f;
            trail.startWidth = GameConfig.StoneRadius * 1.1f;
            trail.endWidth = 0.001f;
            trail.minVertexDistance = 0.004f;
            trail.sharedMaterial = palette.Glow;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            trail.autodestruct = false;

            var stone = go.AddComponent<Stone>();
            stone.BodyRenderer = body.GetComponent<MeshRenderer>();
            stone.Trail = trail;

            go.SetActive(false);
            return go;
        }

        // ---------------------------------------------------------------- preview

        public static AimPreview BuildAimPreview(Transform parent, Rink rink, Palette palette)
        {
            var go = new GameObject("AimPreview");
            go.transform.SetParent(parent, false);

            var preview = go.AddComponent<AimPreview>();
            preview.Playfield = rink;

            preview.Path = ProceduralArt.Line(go.transform, "Path", palette.Glow, 0.006f);
            preview.Path.positionCount = 0;

            var pip = ProceduralArt.Disc(go.transform, "PowerPip", Vector3.zero, 1f, palette.Glow);
            // The disc is built at radius 1 so the preview can scale it directly.
            pip.transform.localScale = new Vector3(0.02f, 1f, 0.02f);
            preview.PowerPip = pip.transform;

            return preview;
        }

        // ---------------------------------------------------------------- modifiers

        /// <summary>
        /// Creates one instance of every modifier with its props, then hands them to a
        /// director. Components are added only after their props exist, and each modifier is
        /// explicitly deactivated once wired, so nothing is left half-enabled.
        /// </summary>
        public static ModifierDirector BuildModifiers(Transform parent, Transform surface,
                                                      Palette palette)
        {
            var root = new GameObject("Modifiers").transform;
            root.SetParent(parent, false);

            var all = new List<RinkModifier>
            {
                BuildCurrent(root, surface, palette),
                BuildBumpers(root, surface, palette),
                BuildWarpGates(root, surface, palette)
            };

            var director = root.gameObject.AddComponent<ModifierDirector>();
            director.Modifiers = all;
            director.FirstRoundIsClean = true;
            director.DeactivateAll();
            return director;
        }

        private static RinkModifier BuildCurrent(Transform root, Transform surface, Palette palette)
        {
            var go = new GameObject("Modifier_Current");
            go.transform.SetParent(root, false);

            var visuals = new GameObject("Visuals").transform;
            visuals.SetParent(surface, false);

            float bandCenterZ = 0.05f;
            float bandHalfDepth = 0.26f;

            var bandMat = palette.CurrentBand;
            ProceduralArt.Box(visuals, "Band",
                new Vector3(0f, 0.0008f, bandCenterZ),
                new Vector3(GameConfig.RinkWidth, 0.0006f, bandHalfDepth * 2f),
                bandMat, false);

            // Chevrons across the band hint at the flow. They are rotated by the modifier when
            // it picks a direction, so the visual never lies about which way the push goes.
            for (int i = -1; i <= 1; i++)
            {
                ProceduralArt.Box(visuals, $"Chevron{i}",
                    new Vector3(0f, 0.0016f, bandCenterZ + i * 0.12f),
                    new Vector3(GameConfig.RinkWidth * 0.8f, 0.0008f, 0.006f),
                    palette.Glow, false);
            }

            var mod = go.AddComponent<CurrentModifier>();
            mod.DisplayName = "Cross Current";
            mod.Description = "A flow pushes stones sideways - aim upstream";
            mod.Visuals = visuals.gameObject;
            mod.BandCenterZ = bandCenterZ;
            mod.BandHalfDepth = bandHalfDepth;
            mod.Strength = 0.85f;
            mod.RandomiseDirection = true;
            mod.Deactivate();
            return mod;
        }

        private static RinkModifier BuildBumpers(Transform root, Transform surface, Palette palette)
        {
            var go = new GameObject("Modifier_Bumpers");
            go.transform.SetParent(root, false);

            var visuals = new GameObject("Visuals").transform;
            visuals.SetParent(surface, false);

            // Three posts: a centre post that closes the direct line, and two outer posts that
            // leave two threadable gaps rather than one. The player always has a shot.
            float halfW = GameConfig.RinkWidth * 0.5f;
            var offsets = new[]
            {
                new Vector2(0f, 0.10f),
                new Vector2(-halfW * 0.62f, -0.10f),
                new Vector2(halfW * 0.62f, -0.10f)
            };

            float postRadius = 0.022f;
            float postHeight = 0.055f;
            var colliders = new List<Collider>();

            var mod = go.AddComponent<BumperModifier>();

            for (int i = 0; i < offsets.Length; i++)
            {
                var post = ProceduralArt.Cylinder(visuals, $"Post{i}",
                    new Vector3(offsets[i].x, postHeight * 0.5f, offsets[i].y),
                    postRadius, postHeight, palette.PostBody, true);

                var collider = post.GetComponent<Collider>();
                if (collider != null) colliders.Add(collider);

                var reporter = post.AddComponent<BumperPost>();
                reporter.Owner = mod;
            }

            mod.DisplayName = "Bumper Posts";
            mod.Description = "Posts block the straight line - thread it or bank it";
            mod.Visuals = visuals.gameObject;
            mod.PostColliders = colliders.ToArray();
            mod.Bounciness = 0.72f;
            mod.HitSpark = ProceduralArt.Burst(visuals, "HitSpark", palette.PostBody.color,
                                               palette.Glow);
            mod.Deactivate();
            return mod;
        }

        private static RinkModifier BuildWarpGates(Transform root, Transform surface, Palette palette)
        {
            var go = new GameObject("Modifier_WarpGates");
            go.transform.SetParent(root, false);

            var visuals = new GameObject("Visuals").transform;
            visuals.SetParent(surface, false);

            float halfW = GameConfig.RinkWidth * 0.5f;
            var gateMat = ProceduralArt.MakeMaterial(new Color(0.65f, 0.40f, 1.00f), 2.2f);

            // Gate A sits near the left wall on the player's half; the exit gate sits on the
            // far right and faces down the rink, so a stone fed into A is spat out on a new
            // line towards the house. Both gates are visible from the launch point.
            var gateA = BuildGate(visuals, "GateA", new Vector3(-halfW * 0.70f, 0f, -0.05f),
                                  0f, gateMat, palette.Glow);
            var gateB = BuildGate(visuals, "GateB", new Vector3(halfW * 0.70f, 0f, 0.26f),
                                  -22f, gateMat, palette.Glow);

            var mod = go.AddComponent<WarpGateModifier>();
            mod.DisplayName = "Warp Gates";
            mod.Description = "Slide into one gate, come out of the other";
            mod.Visuals = visuals.gameObject;
            mod.GateA = gateA;
            mod.GateB = gateB;
            mod.CaptureRadius = 0.055f;
            mod.ExitSpeedScale = 1.0f;
            mod.EnterBurst = ProceduralArt.Burst(visuals, "EnterBurst", gateMat.color, palette.Glow);
            mod.ExitBurst = ProceduralArt.Burst(visuals, "ExitBurst", gateMat.color, palette.Glow);
            mod.Deactivate();
            return mod;
        }

        private static Transform BuildGate(Transform parent, string name, Vector3 localPosition,
                                           float yawDegrees, Material gateMat, Material glowMat)
        {
            var gate = new GameObject(name).transform;
            gate.SetParent(parent, false);
            gate.localPosition = localPosition;
            gate.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

            // A flat ring on the ice plus two short pillars: readable from the low, flat angle
            // the player actually views the table from.
            ProceduralArt.Ring(gate, "Mouth", new Vector3(0f, 0.0018f, 0f), 0.048f, 0.058f, gateMat);
            ProceduralArt.Disc(gate, "Pool", new Vector3(0f, 0.0010f, 0f), 0.046f,
                               ProceduralArt.MakeMaterial(new Color(0.30f, 0.12f, 0.55f), 0.8f));

            for (int s = -1; s <= 1; s += 2)
            {
                ProceduralArt.Cylinder(gate, $"Pillar{s}",
                    new Vector3(0.055f * s, 0.022f, 0f), 0.006f, 0.044f, gateMat, false);
            }

            // An arrow showing which way stones leave this gate.
            ProceduralArt.Box(gate, "ExitArrow", new Vector3(0f, 0.0020f, 0.045f),
                              new Vector3(0.004f, 0.0008f, 0.04f), glowMat, false);

            return gate;
        }
    }
}
