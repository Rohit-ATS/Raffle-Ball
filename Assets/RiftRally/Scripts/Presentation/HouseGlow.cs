using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Setup;

namespace RiftRally.Presentation
{
    /// <summary>
    /// Makes the house itself react to the state of the round.
    ///
    /// The rings tint towards whoever is currently lying closest, and a crown marks that
    /// stone. This answers the one question a player asks on every single throw - "am I
    /// winning this round?" - without them having to read a number or compare two distances
    /// by eye across a metre of table. The whole scoring rule becomes visible in the colour
    /// of the target.
    /// </summary>
    public class HouseGlow : MonoBehaviour
    {
        [Header("References")]
        public Rink Playfield;

        [Tooltip("Ring renderers, outermost first.")]
        public Renderer[] Rings;

        private Material[] _ringMaterials;
        private Color[] _baseColors;

        private Transform _crown;
        private Material _crownMaterial;

        private Stone _shotStone;
        private float _crownShown;
        private float _flash;
        private PlayerSide _leader = PlayerSide.One;
        private bool _hasLeader;

        private void Start()
        {
            if (Playfield == null)
                Playfield = Rink.Instance != null ? Rink.Instance : FindFirstObjectByType<Rink>();

            CacheRingMaterials();
            BuildCrown();
        }

        private void CacheRingMaterials()
        {
            if (Rings == null) return;

            _ringMaterials = new Material[Rings.Length];
            _baseColors = new Color[Rings.Length];

            for (int i = 0; i < Rings.Length; i++)
            {
                if (Rings[i] == null) continue;
                // Each ring gets its own instance so one can be tinted without the others.
                _ringMaterials[i] = Rings[i].material;
                _baseColors[i] = _ringMaterials[i].HasProperty("_BaseColor")
                    ? _ringMaterials[i].GetColor("_BaseColor")
                    : Color.white;
            }
        }

        private void BuildCrown()
        {
            _crownMaterial = ProceduralArt.MakeGlowMaterial(Color.white, 2.6f);
            var crown = ProceduralArt.Ring(transform, "ShotCrown", Vector3.zero,
                GameConfig.StoneRadius * 1.5f, GameConfig.StoneRadius * 1.85f, _crownMaterial);
            _crown = crown.transform;
            _crown.gameObject.SetActive(false);
        }

        /// <summary>Flashes the house, e.g. when a round is scored.</summary>
        public void Flash() => _flash = 1f;

        private void Update()
        {
            if (Playfield == null) return;

            float dt = Time.deltaTime;
            _flash = Mathf.Max(0f, _flash - dt * 1.4f);

            Stone closest = Playfield.ClosestScoringStone();
            bool inHouse = closest != null
                && Playfield.DistanceToTarget(closest.transform.position) <= ScoreKeeper.ScoringRadius;

            _shotStone = inHouse ? closest : null;

            if (_shotStone != null)
            {
                if (!_hasLeader || _leader != _shotStone.Owner)
                {
                    // The lead changing hands is worth a flash of its own.
                    _flash = 1f;
                    _leader = _shotStone.Owner;
                    _hasLeader = true;
                }
            }
            else
            {
                _hasLeader = false;
            }

            TintRings(dt);
            UpdateCrown(dt);
        }

        private void TintRings(float dt)
        {
            if (_ringMaterials == null) return;

            Color lead = _hasLeader ? _leader.ToColor() : GameConfig.NeutralColor;

            for (int i = 0; i < _ringMaterials.Length; i++)
            {
                var mat = _ringMaterials[i];
                if (mat == null) continue;

                // Inner rings lean further towards the leader's colour, so the gradient still
                // reads as a target rather than as a flat disc of one colour.
                float weight = 0.22f + 0.16f * i;
                Color target = Color.Lerp(_baseColors[i], lead, _hasLeader ? weight : 0f);

                float emission = 1.0f + 0.35f * i + _flash * 2.2f;

                Color current = mat.HasProperty("_BaseColor")
                    ? mat.GetColor("_BaseColor")
                    : target;
                Color next = Easing.Damp(current, target, 6f, dt);

                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", next);
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", next * emission);
            }
        }

        private void UpdateCrown(float dt)
        {
            float target = _shotStone != null ? 1f : 0f;
            _crownShown = Easing.Damp(_crownShown, target, 10f, dt);

            if (_crownShown < 0.01f)
            {
                _crown.gameObject.SetActive(false);
                return;
            }

            _crown.gameObject.SetActive(true);

            if (_shotStone != null)
            {
                Vector3 p = _shotStone.transform.position;
                _crown.position = new Vector3(p.x, Playfield.SurfaceY + 0.0020f, p.z);
            }

            float spin = Time.time * 42f;
            _crown.rotation = Quaternion.Euler(0f, spin, 0f);

            float breathe = 1f + Mathf.Sin(Time.time * 3.4f) * 0.05f;
            _crown.localScale = Vector3.one * breathe * Easing.OutBack(_crownShown);

            Color c = _shotStone != null ? _shotStone.Owner.ToColor() : Color.white;
            Color tinted = new Color(c.r, c.g, c.b, _crownShown * 0.85f);

            if (_crownMaterial.HasProperty("_BaseColor"))
                _crownMaterial.SetColor("_BaseColor", tinted);
            if (_crownMaterial.HasProperty("_EmissionColor"))
                _crownMaterial.SetColor("_EmissionColor", c * (2.4f + _flash * 2f) * _crownShown);
        }
    }
}
