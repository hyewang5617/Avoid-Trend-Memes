using UnityEngine;

namespace MemeDodge
{
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        public event System.Action Jumped;
        public event System.Action Landed;
        [SerializeField] PlayerInput controls;
        [SerializeField] LayerMask groundMask = 1 << 8;
        [SerializeField] float speed = 6f;
        [SerializeField] float acceleration = 40f;
        [SerializeField] float jumpSpeed = 10f;
        [SerializeField] float coyoteTime = .1f;
        [SerializeField] float jumpBuffer = .12f;
        Rigidbody2D body;
        BoxCollider2D shape;
        readonly RaycastHit2D[] hits = new RaycastHit2D[8];
        float lastGrounded = float.NegativeInfinity;
        float lastJump = float.NegativeInfinity;
        int jumpsUsed;
        bool wasGrounded;
        public int JumpsUsed => jumpsUsed;
        public float DoubleJumpHeight => jumpSpeed * jumpSpeed / Mathf.Max(.01f, Mathf.Abs(Physics2D.gravity.y * body.gravityScale));
        public void Configure(PlayerInput input, LayerMask ground) { controls = input; groundMask = ground; }
        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            shape = GetComponent<BoxCollider2D>();
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
        void Update() { if (controls != null && controls.ConsumeJump()) lastJump = Time.time; }
        void FixedUpdate()
        {
            if (controls == null) return;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(groundMask);
            filter.useTriggers = false;
            int count = shape.Cast(Vector2.down, filter, hits, .08f);
            bool touchingGround = false;
            for (int i = 0; i < count; i++)
            {
                var platform = hits[i].collider.GetComponent<PlatformEffector2D>();
                // A one-way platform is ground only when our feet are above its top.
                if (platform != null && platform.enabled && platform.useOneWay
                    && shape.bounds.min.y < hits[i].collider.bounds.max.y - .06f) continue;
                if (hits[i].normal.y > .5f && body.linearVelocity.y <= .1f)
                {
                    touchingGround = true;
                    lastGrounded = Time.time;
                    jumpsUsed = 0;
                }
            }
            if (touchingGround && !wasGrounded) Landed?.Invoke();
            wasGrounded = touchingGround;
            var velocity = body.linearVelocity;
            velocity.x = Mathf.MoveTowards(velocity.x, controls.Direction * speed, acceleration * Time.fixedDeltaTime);
            bool grounded = Time.time - lastGrounded <= coyoteTime;
            if (!grounded && jumpsUsed == 0) jumpsUsed = 1;
            if (Time.time - lastJump <= jumpBuffer && (grounded || jumpsUsed < 2))
            {
                velocity.y = jumpSpeed;
                jumpsUsed = grounded ? 1 : jumpsUsed + 1;
                lastJump = lastGrounded = float.NegativeInfinity;
                Jumped?.Invoke();
            }
            body.linearVelocity = velocity;
        }
        void OnDisable()
        {
            if (body != null) body.linearVelocity = Vector2.zero;
            lastJump = lastGrounded = float.NegativeInfinity;
            jumpsUsed = 0;
            wasGrounded = false;
        }
    }
}
