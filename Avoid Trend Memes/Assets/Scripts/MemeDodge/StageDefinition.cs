using UnityEngine;

namespace MemeDodge
{
    public enum MemePattern { SixSeven, BringTheL, ChatBarrage }

    [CreateAssetMenu(menuName = "MemeDodge/Stage")]
    public sealed class StageDefinition : ScriptableObject
    {
        public string title;
        [TextArea] public string instruction;
        public MemePattern pattern;
        [Header("Stage layout (optional)")]
        public GameObject layoutPrefab;
        [Tooltip("Randomly chosen image obstacle prefabs. Empty uses the original text obstacles.")]
        public GameObject[] memePrefabs;
        public Vector2 laserPosition = new(0, -2.7f);
        public Vector2 laserSize = new(16, .12f);
        [Min(3)] public float duration = 18;
        [Min(.3f)] public float attackInterval = 1.6f;
        [Min(.2f)] public float warningDuration = .8f;
        [Min(.5f)] public float projectileSpeed = 4;
    }
}
