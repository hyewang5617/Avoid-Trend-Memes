using UnityEngine;

namespace MemeDodge
{
    public sealed class Danger : MonoBehaviour
    {
        GameController game;
        public void Configure(GameController controller) => game = controller;
        void OnTriggerEnter2D(Collider2D other) => Hit(other);
        void OnTriggerStay2D(Collider2D other) => Hit(other);
        void Hit(Collider2D other)
        {
            if (game != null && other.GetComponent<PlayerMotor>() != null) game.HitPlayer();
        }
    }
}
