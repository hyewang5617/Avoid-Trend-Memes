using UnityEngine;
using UnityEngine.Events;

namespace MemeDodge
{
    [DisallowMultipleComponent]
    public sealed class WarningArea : MonoBehaviour
    {
        public SpriteRenderer visual;
        [Tooltip("Local width and height. Root scale and rotation can also be adjusted.")]
        public Vector2 size = new(4f, .3f);
        [Min(0)] public float startDelay;
        [Min(.01f)] public float duration = .8f;
        [Min(0)] public float blinkFrequency = 3f;
        [Range(0, 1)] public float minimumAlpha = .08f;
        [Range(0, 1)] public float maximumAlpha = .4f;
        public bool playOnEnable = true;
        public UnityEvent onFinished = new();

        float elapsed;
        bool playing;

        void OnEnable()
        {
            ApplySize();
            if (playOnEnable) Play();
            else Stop();
        }

        void OnDisable() => Stop();

        void OnValidate()
        {
            size.x = Mathf.Max(.01f, size.x);
            size.y = Mathf.Max(.01f, size.y);
            duration = Mathf.Max(.01f, duration);
            startDelay = Mathf.Max(0, startDelay);
            ApplySize();
            if (!Application.isPlaying && visual != null)
            {
                visual.enabled = true;
                SetOpacity(maximumAlpha);
            }
        }

        public void Play()
        {
            ApplySize();
            elapsed = 0;
            playing = true;
            if (visual != null)
            {
                visual.enabled = startDelay <= 0;
                SetOpacity(maximumAlpha);
            }
        }

        public void Stop()
        {
            playing = false;
            if (visual != null) visual.enabled = false;
        }

        void Update()
        {
            if (!playing) return;
            elapsed += Time.deltaTime;
            float time = elapsed - startDelay;
            if (time < 0) return;
            if (time >= duration)
            {
                Stop();
                onFinished.Invoke();
                return;
            }
            if (visual == null) return;
            visual.enabled = true;
            float pulse = .5f + .5f * Mathf.Cos(time * blinkFrequency * Mathf.PI * 2f);
            SetOpacity(Mathf.Lerp(minimumAlpha, maximumAlpha, pulse));
        }

        void ApplySize()
        {
            if (visual == null || visual.sprite == null) return;
            var bounds = visual.sprite.bounds.size;
            visual.transform.localScale = new Vector3(size.x / Mathf.Max(.001f, bounds.x), size.y / Mathf.Max(.001f, bounds.y), 1);
        }

        void SetOpacity(float alpha)
        {
            var color = GameController.Accent;
            color.a = alpha;
            visual.color = color;
        }
    }
}
