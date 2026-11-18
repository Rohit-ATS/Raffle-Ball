using UnityEngine;

namespace RiftRally.Scenery
{
    /// <summary>
    /// A flock of birds, built from triangles and animated in code.
    ///
    /// Birds are the cheapest thing that can make a landscape feel inhabited rather than
    /// modelled, and almost all of that effect comes from the flight path rather than from the
    /// bird. A detailed bird on a circle at constant speed reads as a fairground ride; three
    /// triangles on a path that banks into its turns, loses a little height on the outside of
    /// the curve and beats its wings in bursts with glides between them reads as a bird. So
    /// the geometry here is deliberately minimal - a body and two wing triangles - and the
    /// effort goes into <see cref="BirdStyle"/>.
    ///
    /// The whole flock is driven from one <c>Update</c> on one component rather than a
    /// component per bird. At thirty-odd birds that is the difference between one managed call
    /// per frame and thirty, which on a mobile VR CPU is worth having for free.
    /// </summary>
    public class BirdFlock : MonoBehaviour
    {
        private struct Bird
        {
            public Transform Root;
            public Transform LeftWing;
            public Transform RightWing;

            /// <summary>Position within the formation, in the leader's local frame.</summary>
            public Vector3 Offset;

            /// <summary>Keeps wingbeats out of lockstep across the flock.</summary>
            public float FlapPhase;

            public float FlapRate;
            public float BobPhase;
            public float Scale;
        }

        public BirdStyle Style = BirdStyle.Soaring;

        /// <summary>Centre of the flock's beat, in world space.</summary>
        public Vector3 Centre;

        public float PathRadius = 22f;
        public float Altitude = 16f;

        /// <summary>Radians per second around the path. Negative flies the other way.</summary>
        public float Speed = 0.12f;

        /// <summary>Ground height under the flock, so a low pass cannot clip through a hill.</summary>
        public float GroundHeight;

        private Bird[] _birds;
        private float _time;
        private float _seedPhase;

        /// <summary>
        /// Builds a flock under <paramref name="parent"/>.
        /// </summary>
        public static BirdFlock Create(Transform parent, Landscape landscape, int index,
                                       System.Random random, Vector3 centre, float groundHeight)
        {
            var go = new GameObject($"Flock_{index}");
            go.transform.SetParent(parent, false);

            var flock = go.AddComponent<BirdFlock>();
            flock.Style = landscape.Birds;
            flock.Centre = centre;
            flock.GroundHeight = groundHeight;
            flock._seedPhase = (float)random.NextDouble() * 100f;

            // Each style wants a different scale of movement. Soaring raptors work a wide
            // thermal slowly; songbirds cover a small patch quickly.
            switch (landscape.Birds)
            {
                case BirdStyle.Soaring:
                    flock.PathRadius = 18f + (float)random.NextDouble() * 14f;
                    flock.Speed = 0.09f + (float)random.NextDouble() * 0.05f;
                    break;
                case BirdStyle.Migrating:
                    flock.PathRadius = 34f + (float)random.NextDouble() * 12f;
                    flock.Speed = 0.055f + (float)random.NextDouble() * 0.03f;
                    break;
                case BirdStyle.Skimming:
                    flock.PathRadius = 12f + (float)random.NextDouble() * 9f;
                    flock.Speed = 0.26f + (float)random.NextDouble() * 0.12f;
                    break;
                case BirdStyle.Darting:
                    flock.PathRadius = 7f + (float)random.NextDouble() * 6f;
                    flock.Speed = 0.40f + (float)random.NextDouble() * 0.25f;
                    break;
            }

            if (random.Next(2) == 0) flock.Speed = -flock.Speed;

            flock.Altitude = landscape.BirdHeight * (0.95f + (float)random.NextDouble() * 0.5f);
            flock.BuildBirds(landscape, random);
            return flock;
        }

        private void BuildBirds(Landscape landscape, System.Random random)
        {
            int count = Mathf.Max(1, landscape.BirdsPerFlock);
            float span = Mathf.Max(0.1f, landscape.BirdSpan);

            // One material and one mesh of each kind for the whole flock, so every bird in it
            // batches together.
            // Washed a little towards the horizon colour. A bird thirty metres off is seen
            // through thirty metres of air, so a pure dark silhouette reads as a hole cut in
            // the sky rather than as a bird - which is exactly how the first flocks looked.
            Color plumage = Color.Lerp(landscape.BirdColor, landscape.SkyHorizon, 0.3f);
            Material feather = Setup.ProceduralArt.MakeMaterial(plumage, 0f, 0.1f);
            Mesh wingMesh = SceneryArt.BuildWing(1f, 0.42f);
            Mesh bodyMesh = SceneryArt.BuildWing(1f, 0.30f);

            _birds = new Bird[count];

            for (int i = 0; i < count; i++)
            {
                var root = new GameObject($"Bird_{i}").transform;
                root.SetParent(transform, false);

                float scale = span * (0.82f + (float)random.NextDouble() * 0.36f);

                // Body: a narrow triangle lying flat, pointing forward along +Z.
                SceneryArt.Attach(root, "Body", bodyMesh, feather,
                    new Vector3(0f, 0f, -scale * 0.18f),
                    Quaternion.Euler(0f, -90f, 0f),
                    new Vector3(scale * 0.55f, 1f, scale * 0.5f));

                var left = SceneryArt.Attach(root, "WingL", wingMesh, feather,
                    Vector3.zero, Quaternion.identity,
                    new Vector3(-scale * 0.5f, 1f, scale * 0.5f)).transform;

                var right = SceneryArt.Attach(root, "WingR", wingMesh, feather,
                    Vector3.zero, Quaternion.identity,
                    new Vector3(scale * 0.5f, 1f, scale * 0.5f)).transform;

                _birds[i] = new Bird
                {
                    Root = root,
                    LeftWing = left,
                    RightWing = right,
                    Offset = Vector3.zero,
                    FlapPhase = (float)random.NextDouble() * Mathf.PI * 2f,
                    FlapRate = FlapRateFor(Style) * (0.86f + (float)random.NextDouble() * 0.3f),
                    BobPhase = (float)random.NextDouble() * Mathf.PI * 2f,
                    Scale = scale
                };
            }

            // The formation is laid out in a second pass: it depends on the flock size and
            // on the style, both of which are only fully known once every bird exists.
            ApplyFormation(span, random);
        }

        /// <summary>
        /// A loose position behind and beside the leader. Used by every style except the
        /// migrating skein: a tidy lattice of songbirds would read as a display team rather
        /// than as birds.
        /// </summary>
        private static Vector3 OffsetCloud(int index, float unit, System.Random random)
        {
            float x = ((float)random.NextDouble() - 0.5f) * unit * 3.2f;
            float y = ((float)random.NextDouble() - 0.5f) * unit * 1.1f;
            float z = -index * unit * 0.55f
                      + ((float)random.NextDouble() - 0.5f) * unit;
            return new Vector3(x, y, z);
        }

        private static float FlapRateFor(BirdStyle style) => style switch
        {
            BirdStyle.Soaring => 1.1f,
            BirdStyle.Migrating => 2.4f,
            BirdStyle.Skimming => 3.1f,
            _ => 6.2f
        };

        /// <summary>
        /// Lays out where each bird sits relative to the leader.
        ///
        /// A migrating flock gets a real V, because that shape is the single most readable
        /// thing about geese at a distance. Everything else gets a loose cloud.
        /// </summary>
        private void ApplyFormation(float span, System.Random random)
        {
            if (_birds == null) return;

            float unit = Mathf.Max(0.6f, span * 1.7f);

            for (int i = 0; i < _birds.Length; i++)
            {
                if (i == 0) { _birds[i].Offset = Vector3.zero; continue; }

                if (Style == BirdStyle.Migrating)
                {
                    // Alternating sides, stepping back and out: a skein.
                    int rank = (i + 1) / 2;
                    float side = (i % 2 == 0) ? 1f : -1f;
                    float jitterX = ((float)random.NextDouble() - 0.5f) * unit * 0.25f;
                    float jitterY = ((float)random.NextDouble() - 0.5f) * unit * 0.3f;
                    _birds[i].Offset = new Vector3(side * rank * unit + jitterX,
                                                   jitterY,
                                                   -rank * unit * 0.85f);
                }
                else
                {
                    _birds[i].Offset = OffsetCloud(i, unit, random);
                }
            }
        }

        private void Update()
        {
            if (_birds == null) return;

            _time += Time.deltaTime;

            // Sample the path slightly ahead as well as now, and use the difference as the
            // heading. Deriving orientation from the path instead of storing a velocity means
            // a bird can never end up flying sideways after a frame hitch.
            const float lookAhead = 0.12f;
            Vector3 here = PathPoint(_time);
            Vector3 ahead = PathPoint(_time + lookAhead);

            Vector3 forward = ahead - here;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();

            // Bank into the turn. The sign of the path speed decides which way the flock
            // leans, and a soaring bird leans hardest because it is turning on one wing.
            float bankStrength = Style == BirdStyle.Soaring ? 34f : 22f;
            float bank = -Mathf.Sign(Speed) * bankStrength;

            Quaternion heading = Quaternion.LookRotation(forward, Vector3.up);
            Quaternion banked = heading * Quaternion.Euler(0f, 0f, bank);

            for (int i = 0; i < _birds.Length; i++)
            {
                Bird bird = _birds[i];
                if (bird.Root == null) continue;

                Vector3 bob = Vector3.up
                              * Mathf.Sin(_time * 1.3f + bird.BobPhase)
                              * bird.Scale * 0.35f;

                Vector3 position = here + heading * bird.Offset + bob;

                // Never drop a bird into the ground: a skimming flock passes low on purpose
                // and the terrain under it is not flat.
                float floor = GroundHeight + bird.Scale * 0.6f;
                if (position.y < floor) position.y = floor;

                bird.Root.SetPositionAndRotation(position, banked);

                Flap(bird);
            }
        }

        /// <summary>
        /// Beats the wings.
        ///
        /// The flap is not a plain sine. Real birds of every size beat in bursts and then
        /// glide, and holding the wings still between bursts is what stops a flock looking
        /// mechanical. The burst envelope is a slow sine shaped by a power curve, so most of
        /// the time it is near zero and the wings are extended.
        /// </summary>
        private void Flap(Bird bird)
        {
            if (bird.LeftWing == null || bird.RightWing == null) return;

            float t = _time * bird.FlapRate + bird.FlapPhase;

            float burst = Style == BirdStyle.Soaring
                ? Mathf.Pow(Mathf.Max(0f, Mathf.Sin(_time * 0.21f + bird.FlapPhase)), 6f)
                : Style == BirdStyle.Darting
                    ? 1f
                    : Mathf.Lerp(0.35f, 1f,
                        Mathf.Pow(Mathf.Max(0f, Mathf.Sin(_time * 0.55f + bird.FlapPhase)), 2f));

            // Downstroke is faster and deeper than the upstroke, which is the asymmetry that
            // makes a wingbeat look like it is doing work.
            float raw = Mathf.Sin(t);
            float shaped = raw > 0f ? Mathf.Pow(raw, 0.7f) : -Mathf.Pow(-raw, 1.5f) * 0.65f;

            float angle = shaped * 46f * burst;

            bird.LeftWing.localRotation = Quaternion.Euler(0f, 0f, -angle);
            bird.RightWing.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// The leader's position at a moment in time. One function per style, all periodic,
        /// so a flock never needs resetting or respawning.
        /// </summary>
        private Vector3 PathPoint(float time)
        {
            float phase = time * Speed + _seedPhase;

            switch (Style)
            {
                case BirdStyle.Migrating:
                {
                    // A very wide, very flat ellipse: near enough a straight crossing while
                    // the flock is in view, and it comes back round without a teleport.
                    float x = Mathf.Cos(phase) * PathRadius * 1.8f;
                    float z = Mathf.Sin(phase) * PathRadius * 0.55f;
                    float y = Altitude + Mathf.Sin(phase * 2f) * 1.2f;
                    return Centre + new Vector3(x, y, z);
                }

                case BirdStyle.Skimming:
                {
                    // Long passes that dip towards the ground at the ends of the ellipse,
                    // which over water looks like a bird working the surface.
                    float x = Mathf.Cos(phase) * PathRadius * 1.5f;
                    float z = Mathf.Sin(phase * 0.5f) * PathRadius;
                    float dip = Mathf.Abs(Mathf.Cos(phase));
                    float y = Altitude * Mathf.Lerp(0.3f, 1f, 1f - dip * dip);
                    return Centre + new Vector3(x, y, z);
                }

                case BirdStyle.Darting:
                {
                    // Two out-of-phase circles sum to a wandering path that never repeats
                    // visibly and never needs a random number at runtime.
                    float x = Mathf.Cos(phase) * PathRadius
                              + Mathf.Cos(phase * 2.7f + 1.1f) * PathRadius * 0.45f;
                    float z = Mathf.Sin(phase * 1.3f) * PathRadius
                              + Mathf.Sin(phase * 3.1f) * PathRadius * 0.4f;
                    float y = Altitude + Mathf.Sin(phase * 1.9f) * Altitude * 0.3f;
                    return Centre + new Vector3(x, y, z);
                }

                default:
                {
                    // Soaring: a circle that drifts and loses height on one side, the way a
                    // bird riding a thermal does.
                    float x = Mathf.Cos(phase) * PathRadius;
                    float z = Mathf.Sin(phase) * PathRadius;
                    float y = Altitude + Mathf.Sin(phase) * Altitude * 0.18f
                              + Mathf.Sin(phase * 0.37f) * 2.2f;
                    return Centre + new Vector3(x, y, z);
                }
            }
        }
    }
}
