using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Setup;

namespace RiftRally.Presentation
{
    /// <summary>
    /// Everything a single stone does visually: ice spray, impact sparks, a ground halo that
    /// pins it to the surface, and a settle pulse.
    ///
    /// The halo is the important one. A glowing disc floating over a dark table has almost no
    /// depth cue in stereo at table distance, and players consistently misjudge how far a
    /// stone has travelled. A contact shadow directly under it - which is what the halo is -
    /// restores the depth read for a fraction of the cost of a real shadow.
    /// </summary>
    public class StoneVfx : MonoBehaviour
    {
        private Stone _stone;
        private Rink _rink;

        private Transform _halo;
        private Material _haloMaterial;
        private Transform _cap;
        private Material _capMaterial;

        private ParticleSystem _spray;
        private ParticleSystem.EmissionModule _sprayEmission;
        private ParticleSystem _sparks;

        private float _settleFlash;
        private float _spawnAge;
        private Vector3 _capBaseScale;

        /// <summary>Attaches the full effect set to a freshly spawned stone.</summary>
        public static StoneVfx Attach(Stone stone, Material glowMaterial)
        {
            if (stone == null) return null;
            var vfx = stone.gameObject.AddComponent<StoneVfx>();
            vfx.Build(stone, glowMaterial);
            return vfx;
        }

        private void Build(Stone stone, Material glowMaterial)
        {
            _stone = stone;
            _rink = Rink.Instance;
            Color team = stone.Owner.ToColor();

            // ---- ground halo ----
            _haloMaterial = ProceduralArt.MakeGlowMaterial(team, 1.2f);
            var halo = ProceduralArt.Disc(transform, "Halo",
                new Vector3(0f, -GameConfig.StoneHeight * 0.5f + 0.0012f, 0f),
                GameConfig.StoneRadius * 1.9f, _haloMaterial);
            _halo = halo.transform;

            // ---- top cap, which pulses on impact ----
            var capGo = transform.Find("Cap");
            if (capGo != null)
            {
                _cap = capGo;
                var renderer = _cap.GetComponent<Renderer>();
                if (renderer != null)
                {
                    _capMaterial = ProceduralArt.MakeGlowMaterial(
                        Color.Lerp(team, Color.white, 0.5f), 2.4f);
                    renderer.sharedMaterial = _capMaterial;
                }
                _capBaseScale = _cap.localScale;
            }

            // ---- ice spray, emitted continuously while sliding ----
            _spray = ProceduralArt.Burst(transform, "IceSpray",
                new Color(0.75f, 0.90f, 1f), glowMaterial, 0.006f, 0);

            var sprayMain = _spray.main;
            sprayMain.loop = true;
            sprayMain.duration = 1f;
            sprayMain.startLifetime = 0.28f;
            sprayMain.startSpeed = 0.18f;
            sprayMain.gravityModifier = 0.35f;
            sprayMain.maxParticles = 48;

            _sprayEmission = _spray.emission;
            _sprayEmission.rateOverTime = 0f;
            _sprayEmission.SetBursts(new ParticleSystem.Burst[0]);

            var sprayShape = _spray.shape;
            sprayShape.shapeType = ParticleSystemShapeType.Sphere;
            sprayShape.radius = GameConfig.StoneRadius * 0.8f;

            // ---- impact sparks, fired on collision ----
            _sparks = ProceduralArt.Burst(transform, "ImpactSparks",
                Color.Lerp(team, Color.white, 0.4f), glowMaterial, 0.009f, 16);
            var sparkMain = _sparks.main;
            sparkMain.startSpeed = 0.9f;
            sparkMain.startLifetime = 0.30f;
            sparkMain.gravityModifier = 0.8f;

            _stone.Impacted += OnImpact;
            _stone.Settled += OnSettled;
            _stone.Died += OnDied;
        }

        private void OnDestroy()
        {
            if (_stone == null) return;
            _stone.Impacted -= OnImpact;
            _stone.Settled -= OnSettled;
            _stone.Died -= OnDied;
        }

        private void OnImpact(float impulse, Vector3 point)
        {
            if (_sparks == null) return;

            float t = Mathf.Clamp01(impulse / 0.6f);
            int count = Mathf.RoundToInt(Mathf.Lerp(5f, 26f, t));

            _sparks.transform.position = point;
            var main = _sparks.main;
            main.startSpeed = Mathf.Lerp(0.35f, 1.5f, t);
            _sparks.Emit(count);

            _settleFlash = Mathf.Max(_settleFlash, Mathf.Lerp(0.3f, 1f, t));

            // A hard hit is the most dramatic thing that happens in a round, so it is also
            // the moment worth stretching time for.
            if (impulse > 0.22f) SlowMotion.Request(Mathf.Lerp(0.45f, 0.75f, 1f - t), 0.5f);
        }

        private void OnSettled(Stone stone) => _settleFlash = Mathf.Max(_settleFlash, 0.55f);

        private void OnDied(Stone stone)
        {
            if (_spray != null) _sprayEmission.rateOverTime = 0f;
            if (_sparks != null)
            {
                var main = _sparks.main;
                main.startSpeed = 0.5f;
                _sparks.Emit(14);
            }
        }

        private void LateUpdate()
        {
            if (_stone == null) return;

            float dt = Time.deltaTime;
            _spawnAge += dt;

            bool sliding = _stone.State == StoneState.InFlight;
            float speed01 = Mathf.Clamp01(_stone.Speed / GameConfig.MaxLaunchSpeed);

            // ---- spray scales with speed; none at all when still ----
            if (_spray != null)
            {
                _sprayEmission.rateOverTime = sliding ? Mathf.Lerp(0f, 90f, speed01) : 0f;
            }

            // ---- halo tightens and brightens with speed, and sits flat on the ice ----
            if (_halo != null && _rink != null)
            {
                float lift = _rink.SurfaceY + 0.0014f - transform.position.y;
                _halo.localPosition = new Vector3(0f, lift, 0f);
                _halo.rotation = Quaternion.identity;

                float grow = Mathf.Lerp(1f, 1.35f, speed01);
                _halo.localScale = Easing.Damp(_halo.localScale, Vector3.one * grow, 10f, dt);

                float alpha = Mathf.Lerp(0.22f, 0.5f, speed01) + _settleFlash * 0.4f;
                // A short fade-in stops a newly docked stone from popping into existence.
                alpha *= Easing.OutCubic(Mathf.Clamp01(_spawnAge / 0.35f));
                SetGlow(_haloMaterial, _stone.Owner.ToColor(), alpha, 1.1f + _settleFlash * 2f);
            }

            // ---- cap pulse on impact and settle ----
            if (_cap != null)
            {
                float pop = 1f + Easing.Pulse(_settleFlash) * 0.35f;
                _cap.localScale = Vector3.Lerp(_capBaseScale, _capBaseScale * pop, 1f);

                Color team = _stone.Owner.ToColor();
                SetGlow(_capMaterial, Color.Lerp(team, Color.white, 0.5f + _settleFlash * 0.5f),
                        1f, 2.2f + _settleFlash * 4f);
            }

            _settleFlash = Mathf.Max(0f, _settleFlash - dt * 2.4f);
        }

        private static void SetGlow(Material material, Color color, float alpha, float emission)
            => ProceduralArt.SetGlowFade(material, color, alpha, emission);
    }
}
