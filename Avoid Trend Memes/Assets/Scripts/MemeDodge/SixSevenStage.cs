using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MemeDodge
{
    public sealed class SixSevenStage : MonoBehaviour
    {
        public GameObject facePrefab, handPrefab, warningPrefab;
        public AudioClip cheerAudio, sixSevenAudio;
        public float platformY = -1;
        public float platformX = 5;
        public float platformWidth = 3;
        public float handHeight = 3;
        public int CurrentPanel { get; private set; }
        public int HandRepeats { get; private set; }
        public int SlamRepeats { get; private set; }
        public bool Completed { get; private set; }
        public float Duration => 3 + sixSevenAudio.length * (SlamRepeats + HandRepeats);
        GameController game;
        GameObject actors;
        AudioSource voice;
        Coroutine routine;
        readonly List<int> patterns = new();
        sealed class FloorState
        {
            public GameObject target;
            public Vector3 position;
            public Collider2D[] colliders;
            public bool[] enabled;
        }
        readonly List<FloorState> hiddenFloors = new();
        float floorAnimationStart;
        public IReadOnlyList<int> PatternOrder => patterns;

        public void Begin(GameController controller)
        {
            Stop(); game = controller; Completed = false;
            HandRepeats = Random.Range(2, 4); SlamRepeats = Random.Range(2, 4);
            patterns.Clear();
            for (int i = 0; i < HandRepeats; i++) patterns.Add(2);
            for (int i = 0; i < SlamRepeats; i++) patterns.Add(3);
            for (int i = patterns.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (patterns[i], patterns[j]) = (patterns[j], patterns[i]);
            }
            actors = new GameObject("SixSevenStoryboardActors");
            actors.transform.SetParent(transform, false);
            Platform(-platformX); Platform(platformX);
            floorAnimationStart = Time.time;
            foreach (var floor in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
                if (floor.name == "Floor" && floor.gameObject.activeInHierarchy &&
                    !hiddenFloors.Exists(state => state.target == floor.gameObject))
                {
                    var colliders = floor.GetComponentsInChildren<Collider2D>(true);
                    var state = new FloorState { target = floor.gameObject, position = floor.transform.position,
                        colliders = colliders, enabled = new bool[colliders.Length] };
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        state.enabled[i] = colliders[i].enabled;
                        colliders[i].enabled = false;
                    }
                    hiddenFloors.Add(state);
                }
            RecoverToPlatform();
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false; voice.spatialBlend = 0;
            routine = StartCoroutine(Perform());
        }

        void Update()
        {
            if (actors == null) return;
            float t = Mathf.Clamp01((Time.time - floorAnimationStart) / Mathf.Max(.01f, cheerAudio.length));
            float eased = t * t * (3 - 2 * t);
            foreach (var floor in hiddenFloors)
            {
                if (floor.target == null) continue;
                floor.target.transform.position = floor.position + Vector3.down * (8 * eased);
                if (t >= 1) floor.target.SetActive(false);
            }
        }

        void Platform(float x)
        {
            var instance = new GameObject(x < 0 ? "LeftPlatform" : "RightPlatform");
            instance.transform.SetParent(actors.transform, false);
            instance.transform.position = new Vector2(x, platformY);
            instance.transform.localScale = new Vector3(platformWidth, .2f, 1);
            instance.layer = 8;
            var visual = instance.AddComponent<SpriteRenderer>();
            visual.sprite = game.solidSprite; visual.color = GameController.Accent; visual.sortingOrder = 8;
            instance.AddComponent<BoxCollider2D>(); instance.AddComponent<PlatformEffector2D>();
            instance.AddComponent<OneWayPlatform>();
        }

        Transform Figure(GameObject prefab, string label, Vector2 position, float height, bool harmful)
        {
            var instance = Instantiate(prefab, actors.transform);
            instance.name = label; instance.transform.position = position;
            var render = instance.GetComponentInChildren<SpriteRenderer>();
            // Use transformed sprite geometry so newly scaled sprites do not use cached bounds.
            var local = render.sprite.bounds;
            float size = Vector3.Distance(render.transform.TransformPoint(new Vector3(0, local.min.y)), render.transform.TransformPoint(new Vector3(0, local.max.y)));
            instance.transform.localScale *= height / Mathf.Max(.001f, size);
            render.sortingOrder = harmful ? 6 : 0;
            foreach (var hit in instance.GetComponentsInChildren<Collider2D>(true)) hit.enabled = harmful;
            foreach (var danger in instance.GetComponentsInChildren<Danger>(true)) danger.Configure(game);
            Physics2D.SyncTransforms();
            return instance.transform;
        }

        WarningArea Warning(Vector2 position, Vector2 size, float duration)
        {
            var instance = Instantiate(warningPrefab, actors.transform);
            instance.transform.position = position;
            var warning = instance.GetComponent<WarningArea>();
            warning.size = size; warning.duration = duration; warning.Play();
            return warning;
        }

        void Remove(Component item) { if (item != null) { item.gameObject.SetActive(false); Destroy(item.gameObject); } }
        void Play(AudioClip clip) { voice.clip = clip; voice.loop = true; voice.Play(); }

        IEnumerator Perform()
        {
            CurrentPanel = 1;
            var face = Figure(facePrefab, "67CentreArt", new Vector2(0, -8), 6, false);
            var warning = Warning(new Vector2(0, 2), new Vector2(5, 4), 1);
            Play(cheerAudio);
            yield return new WaitForSeconds(1);
            Remove(warning);
            float start = Time.time;
            while (Time.time - start < 2)
            {
                float t = Mathf.Clamp01((Time.time - start) / 2);
                face.position = Vector2.Lerp(new Vector2(0, -8), Vector2.zero, t);
                yield return null;
            }
            Remove(warning);
            // The centre portrait is scenery. Hands and text provide the damage hitboxes.
            foreach (int pattern in patterns)
            {
                CurrentPanel = pattern;
                if (pattern == 2) yield return Hands();
                else yield return Slam();
            }
            voice.Stop(); Completed = true;
        }

        IEnumerator Hands()
        {
            float surface = platformY + .1f;
            float size = handHeight * 1.5f;
            float low = surface - size * .5f - .3f;
            float high = surface + size * .5f + .3f;
            var left = Figure(handPrefab, "LeftHand", new Vector2(-platformX, low), size, true);
            var right = Figure(handPrefab, "RightHand", new Vector2(platformX, high), size, true);
            right.localScale = new Vector3(-right.localScale.x, right.localScale.y, right.localScale.z);
            float windup = Mathf.Min(.3f, sixSevenAudio.length * .2f);
            var a = Warning(new Vector2(-platformX, surface), new Vector2(platformWidth, 1), windup);
            var b = Warning(new Vector2(platformX, surface), new Vector2(platformWidth, 1), windup);
            Play(sixSevenAudio);
            float start = Time.time, half = sixSevenAudio.length * .5f;
            while (Time.time - start < sixSevenAudio.length)
            {
                float elapsed = Time.time - start;
                float t = elapsed < half
                    ? Mathf.Clamp01((elapsed - windup) / Mathf.Max(.01f, half - windup))
                    : 1 - Mathf.Clamp01((elapsed - half) / Mathf.Max(.01f, half));
                left.position = new Vector2(-platformX, Mathf.Lerp(low, high, t));
                right.position = new Vector2(platformX, Mathf.Lerp(high, low, t));
                yield return null;
            }
            Remove(left); Remove(right); Remove(a); Remove(b);
        }

        Transform Digit(string label, float x)
        {
            var instance = new GameObject(label);
            instance.transform.SetParent(actors.transform, false);
            instance.transform.position = new Vector2(x, Camera.main.transform.position.y + Camera.main.orthographicSize + 1.5f);
            var text = instance.AddComponent<TextMesh>();
            text.text = label; text.font = game.worldFont; text.fontSize = 80; text.characterSize = .15f;
            text.anchor = TextAnchor.MiddleCenter; text.color = GameController.Accent;
            var render = instance.GetComponent<MeshRenderer>();
            render.sharedMaterial = game.worldFont.material; render.sortingOrder = 9;
            var bounds = render.bounds;
            float scale = 2.6f / Mathf.Max(.001f, bounds.size.y);
            var hit = instance.AddComponent<BoxCollider2D>();
            hit.size = new Vector2(Mathf.Max(.1f, bounds.size.x), Mathf.Max(.1f, bounds.size.y));
            hit.offset = instance.transform.InverseTransformPoint(bounds.center); hit.isTrigger = true;
            instance.AddComponent<Danger>().Configure(game);
            instance.transform.localScale = Vector3.one * scale;
            return instance.transform;
        }

        IEnumerator Slam()
        {
            var six = Digit("6", -platformX);
            var seven = Digit("7", platformX);
            float startY = six.position.y;
            var sixHit = six.GetComponent<BoxCollider2D>();
            var sevenHit = seven.GetComponent<BoxCollider2D>();
            float sixBottom = six.TransformPoint(sixHit.offset - Vector2.up * sixHit.size.y * .5f).y - six.position.y;
            float sevenBottom = seven.TransformPoint(sevenHit.offset - Vector2.up * sevenHit.size.y * .5f).y - seven.position.y;
            float endSix = platformY + .1f - sixBottom - 1;
            float endSeven = platformY + .1f - sevenBottom - 1;
            float half = sixSevenAudio.length * .5f;
            float windup = Mathf.Min(.15f, half * .2f);
            var a = Warning(new Vector2(-platformX, platformY + 1.4f), new Vector2(platformWidth, 2.6f), windup);
            var b = Warning(new Vector2(platformX, platformY + 1.4f), new Vector2(platformWidth, 2.6f), windup);
            Play(sixSevenAudio);
            float start = Time.time;
            while (Time.time - start < sixSevenAudio.length)
            {
                float elapsed = Time.time - start;
                if (elapsed < half)
                {
                    float t = Mathf.Clamp01((elapsed - windup) / Mathf.Max(.01f, half * .65f - windup));
                    six.position = new Vector2(-platformX, Mathf.Lerp(startY, endSix, t * t));
                    seven.position = new Vector2(platformX, startY);
                }
                else
                {
                    float t = Mathf.Clamp01((elapsed - half) / Mathf.Max(.01f, half * .65f));
                    six.position = new Vector2(-platformX, Mathf.Lerp(endSix, startY, t));
                    seven.position = new Vector2(platformX, Mathf.Lerp(startY, endSeven, t * t));
                }
                yield return null;
            }
            Remove(six); Remove(seven); Remove(a); Remove(b);
        }

        public void RecoverToPlatform()
        {
            if (game == null || game.player == null) return;
            float x = game.player.transform.position.x <= 0 ? -platformX : platformX;
            float halfHeight = game.player.GetComponent<BoxCollider2D>().bounds.extents.y;
            var position = new Vector2(x, platformY + .1f + halfHeight + .04f);
            game.player.GetComponent<Rigidbody2D>().position = position;
            game.player.transform.position = position;
            game.player.ResetAfterTeleport();
            Physics2D.SyncTransforms();
        }

        public void Stop()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (voice != null) { voice.Stop(); Destroy(voice); voice = null; }
            if (actors != null) { actors.SetActive(false); Destroy(actors); actors = null; }
            foreach (var floor in hiddenFloors)
            {
                if (floor.target == null) continue;
                floor.target.transform.position = floor.position;
                floor.target.SetActive(true);
                for (int i = 0; i < floor.colliders.Length; i++)
                    if (floor.colliders[i] != null) floor.colliders[i].enabled = floor.enabled[i];
            }
            hiddenFloors.Clear();
        }
        void OnDisable() => Stop();
    }
}
