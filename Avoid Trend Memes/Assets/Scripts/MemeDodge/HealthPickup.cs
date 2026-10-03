using UnityEngine;

namespace MemeDodge
{
    public sealed class HealthPickup : MonoBehaviour
    {
        GameController game;
        bool collected;
        public void Configure(GameController controller) => game = controller;
        void OnTriggerEnter2D(Collider2D other)
        {
            if (collected || other.GetComponent<PlayerMotor>() == null) return;
            collected = true;
            game.TryCollectHealthPack();
        }
    }
}
