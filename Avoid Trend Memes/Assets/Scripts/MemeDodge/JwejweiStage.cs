using System.Collections;
using UnityEngine;

namespace MemeDodge
{
    public sealed class JwejweiStage : MonoBehaviour
    {
        public AudioClip[] phraseAudio = new AudioClip[5];
        public AudioClip t1Audio, choirAudio;
        public GameObject t1Prefab, warningPrefab;
        public float warningSeconds = .25f;
        public float textHeight = .9f;
        public float volleyInterval = .5f;
        public int CurrentPanel { get; private set; }
        public bool Completed { get; private set; }
        public float Duration
        {
            get
            {
                float value = t1Audio.length + choirAudio.length;
                foreach (var clip in phraseAudio) value += clip.length;
                return value;
            }
        }
        static readonly string[] Words = { "엄", "아예", "아뇨아뇨", "쥐랄", "요이" };
        GameController game;
        GameObject actors;
        AudioSource voice;
        float left, right, top;
        const float Ground = -3.3f;

        public void Begin(GameController controller)
        {
            Stop(); game = controller; Completed = false;
            var camera = Camera.main;
            left = camera.transform.position.x - camera.orthographicSize * camera.aspect;
            right = camera.transform.position.x + camera.orthographicSize * camera.aspect;
            top = camera.transform.position.y + camera.orthographicSize;
            actors = new GameObject("JwejweiStoryboardActors"); actors.transform.SetParent(transform, false);
            voice = gameObject.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.loop = false;
            StartCoroutine(Perform());
        }

        void Play(AudioClip clip) { voice.clip = clip; voice.Play(); }
        static void Remove(Transform item)
        { if (item != null) { item.gameObject.SetActive(false); Destroy(item.gameObject); } }

        Transform Warning(Vector2 position, Vector2 size, float duration, float angle = 0)
        {
            var item = Instantiate(warningPrefab, actors.transform);
            item.name = "JwejweiWarning"; item.transform.position = position;
            item.transform.rotation = Quaternion.Euler(0, 0, angle);
            var warning = item.GetComponent<WarningArea>();
            warning.size = size; warning.duration = duration; warning.Play();
            return item.transform;
        }

        float DistanceToEdge(Vector2 point, Vector2 direction)
        {
            float x = Mathf.Abs(direction.x) < .0001f ? float.PositiveInfinity
                : ((direction.x > 0 ? right : left) - point.x) / direction.x;
            float y = Mathf.Abs(direction.y) < .0001f ? float.PositiveInfinity
                : ((direction.y > 0 ? top : Ground) - point.y) / direction.y;
            return Mathf.Min(x, y);
        }

        IEnumerator TextAttack(int word, float warnTime, float travelTime, bool guided = false, float sizeMultiplier = 1)
        {
            var item = new GameObject("JwejweiText_" + Words[word]);
            item.transform.SetParent(actors.transform, false);
            var text = item.AddComponent<TextMesh>();
            text.font = game.worldFont; text.text = Words[word]; text.fontSize = 80; text.characterSize = .15f;
            text.anchor = TextAnchor.MiddleCenter; text.color = GameController.Accent;
            var render = item.GetComponent<MeshRenderer>(); render.sharedMaterial = game.worldFont.material; render.sortingOrder = 12;
            var bounds = render.bounds;
            float shotHeight = textHeight * sizeMultiplier;
            float scale = shotHeight / Mathf.Max(.001f, bounds.size.y);
            var hit = item.AddComponent<BoxCollider2D>(); hit.isTrigger = true; hit.enabled = false;
            hit.size = new Vector2(Mathf.Max(.1f, bounds.size.x), Mathf.Max(.1f, bounds.size.y));
            hit.offset = item.transform.InverseTransformPoint(bounds.center);
            item.AddComponent<Danger>().Configure(game);
            item.transform.localScale = Vector3.one * scale;
            float angle = Random.Range(0f, 360f);
            Vector2 direction = new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector2 point = new(Random.Range(left * .75f, right * .75f), Random.Range(Ground + .4f, top - .4f));
            float margin = bounds.extents.magnitude * scale + .2f;
            Vector2 from = point - direction * (DistanceToEdge(point, -direction) + margin);
            Vector2 to = point + direction * (DistanceToEdge(point, direction) + margin);
            item.transform.position = from; render.enabled = false;
            var warning = Warning((from + to) * .5f, new Vector2(Vector2.Distance(from, to), shotHeight), warnTime, angle);
            if (guided)
            {
                float warningLength = Vector2.Distance(from, to);
                float warningStart = Time.time;
                while (Time.time - warningStart < warnTime)
                {
                    point = game.player.transform.position;
                    point.x = Mathf.Clamp(point.x, left + .01f, right - .01f);
                    point.y = Mathf.Clamp(point.y, Ground + .01f, top - .01f);
                    direction = (point - from).normalized;
                    to = point + direction * (DistanceToEdge(point, direction) + margin);
                    warning.position = (from + to) * .5f;
                    warning.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                    warning.localScale = new Vector3(Vector2.Distance(from, to) / Mathf.Max(.001f, warningLength), 1, 1);
                    yield return null;
                }
            }
            else yield return new WaitForSeconds(warnTime);
            Remove(warning); render.enabled = true; hit.enabled = true;
            float start = Time.time;
            while (Time.time - start < travelTime)
            {
                item.transform.position = Vector2.Lerp(from, to, Mathf.Clamp01((Time.time - start) / travelTime));
                yield return null;
            }
            Remove(item.transform);
        }

        IEnumerator Perform()
        {
            for (int i = 0; i < Words.Length; i++)
            {
                CurrentPanel = i + 1; Play(phraseAudio[i]);
                float warn = Mathf.Min(warningSeconds, phraseAudio[i].length * .25f);
                float start = Time.time;
                StartCoroutine(TextAttack(i, warn, Mathf.Max(.01f, phraseAudio[i].length - warn), guided: true, sizeMultiplier: 1.5f));
                while (Time.time - start < phraseAudio[i].length) yield return null;
            }
            CurrentPanel = 6; Play(t1Audio);
            float t1Start = Time.time;
            var warning = Warning(new Vector2((left + right) * .5f, Ground + 1), new Vector2((right - left) * .9f, 2), warningSeconds);
            var image = Instantiate(t1Prefab, actors.transform);
            image.name = "GrowingT1";
            foreach (var danger in image.GetComponentsInChildren<Danger>(true)) danger.Configure(game);
            var sprite = image.GetComponentInChildren<SpriteRenderer>(); sprite.sortingOrder = 7;
            var baseScale = image.transform.localScale;
            var local = sprite.sprite.bounds;
            float width = Vector3.Distance(sprite.transform.TransformPoint(new Vector3(local.min.x, 0)),
                sprite.transform.TransformPoint(new Vector3(local.max.x, 0)));
            float finalScale = (right - left) * .9f / Mathf.Max(.001f, width);
            image.SetActive(false);
            yield return new WaitForSeconds(warningSeconds);
            Remove(warning); image.SetActive(true);
            while (Time.time - t1Start < t1Audio.length)
            {
                float t = Mathf.Clamp01((Time.time - t1Start - warningSeconds) / Mathf.Max(.01f, t1Audio.length - warningSeconds));
                image.transform.localScale = baseScale * finalScale * Mathf.Lerp(.15f, 1, t);
                image.transform.position = new Vector2((left + right) * .5f, 0);
                Physics2D.SyncTransforms();
                float minY = float.PositiveInfinity;
                foreach (var hit in image.GetComponentsInChildren<Collider2D>()) minY = Mathf.Min(minY, hit.bounds.min.y);
                image.transform.position += Vector3.up * (Ground - minY);
                yield return null;
            }
            Remove(image.transform);
            CurrentPanel = 7; Play(choirAudio);
            float choirStart = Time.time, next = 0;
            while (Time.time - choirStart < choirAudio.length)
            {
                float elapsed = Time.time - choirStart;
                if (elapsed >= next && choirAudio.length - elapsed > .2f)
                {
                    next += Mathf.Max(.01f, volleyInterval);
                    int count = Random.Range(2, 5);
                    for (int i = 0; i < count; i++)
                    {
                        float warn = .15f;
                        float flight = Mathf.Min(Random.Range(.9f, 1.4f), choirAudio.length - elapsed - warn);
                        StartCoroutine(TextAttack(Random.Range(0, Words.Length), warn, flight));
                    }
                }
                yield return null;
            }
            voice.Stop(); Completed = true;
        }

        public void Stop()
        {
            StopAllCoroutines();
            if (voice != null) { voice.Stop(); Destroy(voice); voice = null; }
            if (actors != null) { actors.SetActive(false); Destroy(actors); actors = null; }
        }
        void OnDisable() => Stop();
    }
}
