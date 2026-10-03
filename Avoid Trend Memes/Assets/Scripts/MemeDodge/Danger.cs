using UnityEngine;

namespace MemeDodge
{
    public sealed class Danger : MonoBehaviour
    {
        GameController game;
        bool protectsSafeZone;
        Rect safeZone;
        public void Configure(GameController controller) => game = controller;
        public void ConfigureSafeZone(Rect region) { safeZone = region; protectsSafeZone = true; }
        void OnTriggerEnter2D(Collider2D other) => Hit(other);
        void OnTriggerStay2D(Collider2D other) => Hit(other);
        void Hit(Collider2D other)
        {
            if (game == null || other.GetComponent<PlayerMotor>() == null) return;
            Vector2 centre = other.bounds.center;
            if (protectsSafeZone && centre.x >= safeZone.xMin && centre.x <= safeZone.xMax
                && centre.y >= safeZone.yMin && centre.y <= safeZone.yMax) return;
            game.HitPlayer();
        }
    }
}
