using UnityEngine;
using UnityEngine.XR;

namespace RiftRally.Setup
{
    /// <summary>
    /// Picks the render settings, and explains the one trade that actually matters in VR.
    ///
    /// "Higher resolution" in a headset does not mean a 4K output image. The panel resolution
    /// is fixed; what the game controls is the size of the eye buffer it renders before that
    /// buffer is sampled onto the panel. Rendering above the panel's resolution and
    /// downsampling - supersampling - is what removes the shimmer on thin bright geometry,
    /// and this game is almost entirely thin bright geometry: ring outlines, aim lines,
    /// trails. So the eye texture scale is the single highest-value quality dial here, far
    /// above anything texture related, and it costs nothing in download size.
    ///
    /// The frame rate budget is the hard constraint: dropping below the headset's refresh
    /// rate in VR is not "a bit choppy", it is nauseating. Every setting below is chosen to
    /// hold 90 Hz on a Quest 2 with headroom, and the supersampling level is the first thing
    /// to lower if a device cannot keep up.
    /// </summary>
    public class QualityTuner : MonoBehaviour
    {
        public enum Preset
        {
            /// <summary>Maximum crispness. Quest 3 and better.</summary>
            Crisp,

            /// <summary>Balanced. The default, comfortable on a Quest 2.</summary>
            Balanced,

            /// <summary>Lowest cost, for weak devices or when frames are being missed.</summary>
            Fast
        }

        [Tooltip("Starting quality preset.")]
        public Preset Quality = Preset.Balanced;

        [Tooltip("Landscape owner, thinned out along with the render settings.")]
        public Scenery.LandscapeDirector Scenery;

        [Tooltip("Target refresh rate. VR must hold this; 90 Hz is the Quest default.")]
        public int TargetFrameRate = 90;

        [Tooltip("Watch the frame rate and drop the preset if frames are being missed.")]
        public bool AdaptiveQuality = true;

        [Tooltip("Seconds of sustained slow frames before stepping down.")]
        public float AdaptWindow = 4f;

        [Tooltip("Seconds to ignore after start, while the scene is still warming up.")]
        public float WarmupSeconds = 6f;

        private float _slowTime;
        private float _age;
        private bool _hasSteppedDown;

        private void Start()
        {
            Apply(Quality);
        }

        public void Apply(Preset preset)
        {
            Quality = preset;

            // Supersampling: the main dial. Above 1.0 renders larger than the panel and
            // downsamples, which is what kills the shimmer on thin glowing lines.
            float eyeScale = preset switch
            {
                Preset.Crisp => 1.4f,
                Preset.Balanced => 1.2f,
                _ => 1.0f
            };

            int msaa = preset switch
            {
                Preset.Crisp => 4,
                Preset.Balanced => 4,
                _ => 2
            };

            // MSAA, not post-process antialiasing. On a mobile GPU with a tiled renderer MSAA
            // is close to free, and unlike FXAA or TAA it does not blur or smear - and a
            // smeared aim line in a throwing game is a real loss of information.
            QualitySettings.antiAliasing = msaa;
            QualitySettings.vSyncCount = 0;          // the XR runtime paces frames, not vsync
            QualitySettings.shadowDistance = 6f;      // table-scale game; no distant shadows
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowCascades = 1;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            QualitySettings.particleRaycastBudget = 16;
            QualitySettings.softParticles = false;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.billboardsFaceCameraPosition = true;
            QualitySettings.lodBias = 1.4f;

            Application.targetFrameRate = TargetFrameRate;

            if (XRSettings.enabled)
            {
                XRSettings.eyeTextureResolutionScale = eyeScale;
                XRSettings.renderViewportScale = 1f;
            }

            // Hands-only game: no need to keep a controller-shaped input path warm.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // The landscape thins out with the render settings. Supersampling is the dial
            // that matters for the rink, but on a device that is already missing frames the
            // hundred and fifty trees on the hillside are the cheapest thing to give up, and
            // a player looking at the table will not notice the far treeline getting sparser.
            if (Scenery != null) Scenery.ApplyQuality(preset);
        }

        private void Update()
        {
            if (!AdaptiveQuality || _hasSteppedDown) return;

            // Loading, shader compilation and the first frames of physics all produce long
            // frames that say nothing about whether the game can hold its budget. Counting
            // them meant the quality dropped permanently a second after launch, every time.
            _age += Time.unscaledDeltaTime;
            if (_age < WarmupSeconds) return;

            float budget = 1f / Mathf.Max(30, TargetFrameRate);

            // unscaledDeltaTime, so the slow-motion effect is not mistaken for a dropped frame.
            if (Time.unscaledDeltaTime > budget * 1.35f) _slowTime += Time.unscaledDeltaTime;
            else _slowTime = Mathf.Max(0f, _slowTime - Time.unscaledDeltaTime * 0.5f);

            if (_slowTime < AdaptWindow) return;

            Preset next = Quality == Preset.Crisp ? Preset.Balanced : Preset.Fast;
            if (next == Quality) { _hasSteppedDown = true; return; }

            Debug.Log($"[RiftRally] Frames being missed; stepping quality {Quality} -> {next}");
            Apply(next);
            _slowTime = 0f;
            _hasSteppedDown = Quality == Preset.Fast;
        }
    }
}
