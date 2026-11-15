using UnityEngine;
using RiftRally.Core;

namespace RiftRally.Gameplay
{
    public enum StoneState
    {
        Docked,     // waiting at the launch point, grabbable
        Held,       // in the player's pinch
        InFlight,   // sliding
        Settled,    // stopped on the rink, counts for scoring
        Dead        // knocked out of bounds
    }

    /// <summary>
    /// A single playing stone.
    ///
    /// Physics deliberately runs in a constrained "2.5D" mode: gravity is off, Y position is
    /// frozen and tilt is locked, so stones slide and collide like discs on a plane. That
    /// makes shots repeatable and readable, which matters far more than simulating a real
    /// curling stone, and it eliminates the jitter and tunnelling that plague thin rolling
    /// colliders at VR frame rates.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class Stone : MonoBehaviour
    {
        public PlayerSide Owner { get; private set; }
        public StoneState State { get; private set; } = StoneState.Docked;

        /// <summary>True when this stone sits on the rink and can score.</summary>
        public bool CountsForScoring => State == StoneState.Settled;

        [Header("Visuals")]
        public Renderer BodyRenderer;
        public TrailRenderer Trail;
        public Light GlowLight;

        private Rigidbody _rb;
        private SphereCollider _collider;
        private float _slowTimer;
        private float _lifeTime;
        private Vector3 _lastContactPoint;

        /// <summary>Raised when this stone comes to rest. Argument is the stone itself.</summary>
        public event System.Action<Stone> Settled;

        /// <summary>Raised when this stone leaves the rink. Argument is the stone itself.</summary>
        public event System.Action<Stone> Died;

        /// <summary>Raised on a meaningful impact. Arguments: impulse magnitude, contact point.</summary>
        public event System.Action<float, Vector3> Impacted;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _collider = GetComponent<SphereCollider>();

            _rb.useGravity = false;
            _rb.mass = GameConfig.StoneMass;
            _rb.angularDamping = GameConfig.StoneAngularDamping;
            _rb.linearDamping = 0f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.constraints = RigidbodyConstraints.FreezePositionY
                            | RigidbodyConstraints.FreezeRotationX
                            | RigidbodyConstraints.FreezeRotationZ;

            _collider.radius = GameConfig.StoneRadius;
        }

        private void OnDestroy()
        {
            if (Rink.Instance != null) Rink.Instance.Unregister(this);
        }

        /// <summary>Configures ownership and colour, and registers with the rink.</summary>
        public void Initialise(PlayerSide owner)
        {
            Owner = owner;
            Color c = owner.ToColor();

            if (BodyRenderer != null)
            {
                var mat = BodyRenderer.material;
                mat.color = c;
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", c * 1.35f);
                }
            }

            if (Trail != null)
            {
                Trail.startColor = new Color(c.r, c.g, c.b, 0.75f);
                Trail.endColor = new Color(c.r, c.g, c.b, 0f);
                Trail.emitting = false;
            }

            if (GlowLight != null)
            {
                GlowLight.color = c;
                GlowLight.intensity = 0.35f;
            }

            SetState(StoneState.Docked);
            if (Rink.Instance != null) Rink.Instance.Register(this);
        }

        // ---------------------------------------------------------------- state

        public void SetState(StoneState next)
        {
            State = next;

            switch (next)
            {
                case StoneState.Docked:
                    _rb.isKinematic = true;
                    if (Trail != null) { Trail.emitting = false; Trail.Clear(); }
                    break;

                case StoneState.Held:
                    _rb.isKinematic = true;
                    if (Trail != null) Trail.emitting = false;
                    break;

                case StoneState.InFlight:
                    _rb.isKinematic = false;
                    _slowTimer = 0f;
                    _lifeTime = 0f;
                    if (Trail != null) { Trail.Clear(); Trail.emitting = true; }
                    break;

                case StoneState.Settled:
                    // Stays dynamic on purpose: a resting stone must still be knockable,
                    // because takeouts are the core tactical move of the game.
                    _rb.isKinematic = false;
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                    if (Trail != null) Trail.emitting = false;
                    Settled?.Invoke(this);
                    break;

                case StoneState.Dead:
                    _rb.isKinematic = true;
                    if (Trail != null) Trail.emitting = false;
                    if (GlowLight != null) GlowLight.enabled = false;
                    Died?.Invoke(this);
                    break;
            }
        }

        /// <summary>Moves the stone while it is held, keeping it flat on the surface.</summary>
        public void MoveTo(Vector3 worldPosition)
        {
            if (Rink.Instance != null) worldPosition.y = Rink.Instance.StoneCenterY;
            transform.position = worldPosition;
        }

        /// <summary>Releases the stone with a velocity. Spin adds a little character.</summary>
        public void Launch(Vector3 velocity, float spin)
        {
            SetState(StoneState.InFlight);
            velocity.y = 0f;
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = new Vector3(0f, spin, 0f);
        }

        /// <summary>Pushes the stone mid-flight. Used by rink modifiers such as the current.</summary>
        public void AddExternalForce(Vector3 force)
        {
            if (State != StoneState.InFlight) return;
            _rb.AddForce(force, ForceMode.Force);
        }

        /// <summary>Teleports a sliding stone, preserving speed. Used by the warp gate.</summary>
        public void WarpTo(Vector3 worldPosition, Vector3 newDirection)
        {
            if (Rink.Instance != null) worldPosition.y = Rink.Instance.StoneCenterY;
            float speed = _rb.linearVelocity.magnitude;
            transform.position = worldPosition;
            newDirection.y = 0f;
            _rb.linearVelocity = newDirection.normalized * speed;
            if (Trail != null) Trail.Clear();
        }

        public Vector3 Velocity => _rb != null ? _rb.linearVelocity : Vector3.zero;
        public float Speed => Velocity.magnitude;

        // ---------------------------------------------------------------- simulation

        private void FixedUpdate()
        {
            // A settled stone is still dynamic, so watch for it being knocked back into play.
            if (State == StoneState.Settled)
            {
                if (_rb.linearVelocity.magnitude > GameConfig.SettleSpeed * 2.2f)
                {
                    SetState(StoneState.InFlight);
                }
                return;
            }

            if (State != StoneState.InFlight) return;

            _lifeTime += Time.fixedDeltaTime;

            // Constant-deceleration friction reads as "ice" far better than linear damping,
            // because the stone keeps its speed early and then dies off predictably.
            Vector3 v = _rb.linearVelocity;
            v.y = 0f;
            float speed = v.magnitude;

            if (speed > 0.0001f)
            {
                float drop = GameConfig.IceFriction * Time.fixedDeltaTime;
                float newSpeed = Mathf.Max(0f, speed - drop);
                _rb.linearVelocity = v.normalized * newSpeed;
            }

            // Settle detection.
            if (_rb.linearVelocity.magnitude < GameConfig.SettleSpeed)
            {
                _slowTimer += Time.fixedDeltaTime;
                if (_slowTimer >= GameConfig.SettleHoldTime) SetState(StoneState.Settled);
            }
            else
            {
                _slowTimer = 0f;
            }

            // Fell off the rink.
            if (Rink.Instance != null && !Rink.Instance.IsInsideBounds(transform.position))
            {
                SetState(StoneState.Dead);
                return;
            }

            // Safety valve: never let a stone hold up the round forever.
            if (_lifeTime > 22f) SetState(StoneState.Settled);
        }

        private void OnCollisionEnter(Collision collision)
        {
            float impulse = collision.impulse.magnitude;
            if (impulse < 0.02f) return;

            _lastContactPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;

            Impacted?.Invoke(impulse, _lastContactPoint);

            // A stone that gets hit while resting is back in play and must re-settle.
            var other = collision.collider.GetComponentInParent<Stone>();
            if (other != null && State == StoneState.Settled && impulse > 0.05f)
            {
                SetState(StoneState.InFlight);
            }
        }
    }
}
