using UnityEngine;

namespace MemeDodge
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D), typeof(PlatformEffector2D))]
    public sealed class OneWayPlatform : MonoBehaviour
    {
        void Reset() => Configure();
        void Awake() => Configure();
        void OnValidate() => Configure();

        public void Configure()
        {
            var collider = GetComponent<BoxCollider2D>();
            var effector = GetComponent<PlatformEffector2D>();
            collider.isTrigger = false;
            collider.usedByEffector = true;
            effector.useColliderMask = false;
            effector.useOneWay = true;
            effector.useOneWayGrouping = true;
            effector.surfaceArc = 180;
            effector.useSideFriction = false;
            effector.useSideBounce = false;
        }
    }
}
