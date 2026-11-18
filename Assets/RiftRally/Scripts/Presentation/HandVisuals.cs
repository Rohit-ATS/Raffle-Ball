using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Setup;

namespace RiftRally.Presentation
{
    /// <summary>
    /// Draws what the game thinks your hands are doing.
    ///
    /// This is not decoration. In hand-tracked VR the player gets no physical feedback at all,
    /// so without a visible pinch state they cannot tell the difference between "the game did
    /// not register my pinch" and "the game ignored my throw" - and they blame the game either
    /// way. A ring that visibly closes as the fingers close, and a reach halo that lights up
    /// when a stone is grabbable, removes that whole class of confusion.
    ///
    /// Deliberately minimal geometry: two rings and a dot per hand. Full skinned hand meshes
    /// cost frame time and, worse, they occlude the rink the player is aiming across.
    /// </summary>
    public class HandVisuals : MonoBehaviour
    {
        [Header("References")]
        public HandInput Hands;
        public StoneLauncher Launcher;

        [Header("Look")]
        public float RingRadius = 0.028f;
        public float DotRadius = 0.006f;

        private class HandRig
        {
            public Transform Root;
            public Transform PinchRing;
            public Transform Dot;
            public Transform ReachHalo;
            public Renderer RingRenderer;
            public Renderer DotRenderer;
            public Renderer HaloRenderer;
            public Material RingMaterial;
            public Material DotMaterial;
            public Material HaloMaterial;
            public float Shown;
            public float HaloShown;
        }

        private readonly HandRig[] _rigs = new HandRig[2];

        private void Start()
        {
            if (Hands == null)
                Hands = HandInput.Instance != null ? HandInput.Instance : FindFirstObjectByType<HandInput>();
            if (Launcher == null) Launcher = FindFirstObjectByType<StoneLauncher>();

            for (int i = 0; i < 2; i++) _rigs[i] = BuildRig((HandSide)i);
        }

        private HandRig BuildRig(HandSide side)
        {
            var rig = new HandRig();

            var root = new GameObject($"Hand_{side}").transform;
            root.SetParent(transform, false);
            rig.Root = root;

            Color tint = new Color(0.78f, 0.90f, 1f);

            rig.RingMaterial = ProceduralArt.MakeGlowMaterial(tint, 2.2f);
            rig.DotMaterial = ProceduralArt.MakeGlowMaterial(Color.white, 3.0f);
            rig.HaloMaterial = ProceduralArt.MakeGlowMaterial(GameConfig.TargetColor, 1.8f);

            // The pinch ring lies flat so it reads from above, which is where the player is
            // looking when their hand is over the table.
            var ring = ProceduralArt.Ring(root, "PinchRing", Vector3.zero,
                                          RingRadius * 0.74f, RingRadius, rig.RingMaterial);
            rig.PinchRing = ring.transform;
            rig.RingRenderer = ring.GetComponent<Renderer>();

            var dot = ProceduralArt.Disc(root, "Dot", Vector3.zero, DotRadius, rig.DotMaterial);
            rig.Dot = dot.transform;
            rig.DotRenderer = dot.GetComponent<Renderer>();

            // The reach halo is drawn at the grab radius, so "am I close enough" is answered
            // by geometry the player can see rather than by trial and error.
            var halo = ProceduralArt.Ring(root, "ReachHalo", Vector3.zero,
                                          GameConfig.GrabRadius * 0.93f,
                                          GameConfig.GrabRadius, rig.HaloMaterial);
            rig.ReachHalo = halo.transform;
            rig.HaloRenderer = halo.GetComponent<Renderer>();

            SetAlpha(rig.RingMaterial, 0f);
            SetAlpha(rig.DotMaterial, 0f);
            SetAlpha(rig.HaloMaterial, 0f);
            return rig;
        }

        private void Update()
        {
            if (Hands == null) return;

            for (int i = 0; i < 2; i++)
            {
                var rig = _rigs[i];
                if (rig == null) continue;

                HandSample sample = Hands.Get((HandSide)i);
                UpdateRig(rig, sample);
            }
        }

        private void UpdateRig(HandRig rig, HandSample sample)
        {
            float dt = Time.deltaTime;

            // Fade with tracking confidence rather than popping on and off. A hand that is
            // half-tracked should look half-there; that is honest and it stops the flicker.
            float targetShown = sample.IsTracked ? sample.Confidence : 0f;
            rig.Shown = Easing.Damp(rig.Shown, targetShown, 14f, dt);

            if (rig.Shown < 0.01f)
            {
                rig.Root.gameObject.SetActive(false);
                return;
            }
            rig.Root.gameObject.SetActive(true);

            rig.Root.position = sample.PinchPoint;

            // Ring tightens and brightens as the pinch closes: a continuous read on a binary
            // gesture, so the player can feel where the threshold is.
            float squeeze = Mathf.Lerp(1f, 0.46f, Easing.SmoothStep(sample.PinchStrength));
            Vector3 ringScale = Vector3.one * squeeze;
            rig.PinchRing.localScale = Easing.Damp(rig.PinchRing.localScale, ringScale, 22f, dt);

            Color ringColor = sample.IsPinching
                ? GameConfig.TargetColor
                : Color.Lerp(new Color(0.78f, 0.90f, 1f), GameConfig.TargetColor,
                             sample.PinchStrength);
            SetColor(rig.RingMaterial, ringColor, rig.Shown * Mathf.Lerp(0.45f, 1f, sample.PinchStrength));

            rig.Dot.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.35f, sample.PinchStrength);
            SetColor(rig.DotMaterial, Color.white, rig.Shown * 0.9f);

            // Halo only while there is something to grab, and only when roughly in range.
            bool grabbable = Launcher != null
                             && Launcher.State == LauncherState.Ready
                             && Launcher.ActiveStone != null;

            float haloTarget = 0f;
            if (grabbable)
            {
                float reach = Vector3.Distance(sample.PinchPoint,
                                               Launcher.ActiveStone.transform.position);
                // Full strength when inside the grab radius, fading out over the next 12 cm.
                haloTarget = Mathf.Clamp01(1f - (reach - GameConfig.GrabRadius) / 0.12f);
            }

            rig.HaloShown = Easing.Damp(rig.HaloShown, haloTarget, 10f, dt);
            SetColor(rig.HaloMaterial, GameConfig.TargetColor, rig.HaloShown * rig.Shown * 0.5f);

            float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.04f * rig.HaloShown;
            rig.ReachHalo.localScale = Vector3.one * pulse;
        }

        private static void SetColor(Material material, Color color, float alpha)
            => ProceduralArt.SetGlowFade(material, color, alpha, 1.6f);

        private static void SetAlpha(Material material, float alpha)
            => ProceduralArt.SetGlowFade(material, Color.white, alpha);
    }
}
