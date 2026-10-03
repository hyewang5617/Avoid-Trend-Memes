using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MemeDodge
{
    public sealed class BringLStage : MonoBehaviour
    {
        public GameObject lPrefab, creamPrefab, crossPrefab, vegetablePrefab, selfiePrefab, warningPrefab;
        public AudioClip lAudio, creamAudio, crossAudio, vegetableAudio, selfieAudio;
        [Min(.1f)] public float imageSizeMultiplier = 1.5f;
        [Min(.1f)] public float textSizeMultiplier = 1.5f;
        [Min(1)] public float executionTimeMultiplier = 1.6f;
        public float warningSeconds = .55f;
        [Tooltip("Enable HEY attacks for a future hard mode. Disabled in the default mode.")]
        public bool enableHeyAttacks = false;
        [Tooltip("Enable panel 5 side-L attacks for a future hard mode.")]
        public bool enableSideLAttacks = false;
        public Vector2 safeCentre = new(0, -.9f);
        public Vector2 safeSize = new(1.3f, 1.3f);
        public int CurrentPanel { get; private set; }
        public bool Completed { get; private set; }
        public float Duration => .7f + (enableSideLAttacks ? 3 : 2) * lAudio.length * 2
            + lAudio.length
            + crossAudio.length * executionTimeMultiplier + selfieAudio.length
            + creamAudio.length + vegetableAudio.length + .6f;
        GameController game;
        GameObject actors;
        AudioSource voice;
        Coroutine routine;
        float width, height, top, bottom;
        readonly List<Shot> shots = new();
        sealed class Shot
        {
            public Transform target;
            public Vector2 velocity;
            public float age, growth, lifetime = 5;
        }

        public void Begin(GameController controller)
        {
            Stop();
            game = controller;
            Completed = false;
            var camera = Camera.main;
            height = camera.orthographicSize * 2;
            width = height * camera.aspect;
            top = camera.transform.position.y + height * .5f;
            bottom = -3.3f;
            actors = new GameObject("BringLStoryboardActors");
            actors.transform.SetParent(transform, false);
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0;
            routine = StartCoroutine(Perform());
        }

        Transform Figure(GameObject prefab, string label, Vector2 position, float targetHeight)
        {
            var instance = Instantiate(prefab, actors.transform);
            instance.name = label;
            instance.transform.position = position;
            instance.transform.localScale *= targetHeight * imageSizeMultiplier / Mathf.Max(.001f, VisualBounds(instance.transform).size.y);
            foreach (var danger in instance.GetComponentsInChildren<Danger>(true)) danger.Configure(game);
            Physics2D.SyncTransforms();
            return instance.transform;
        }

        static Bounds VisualBounds(Transform actor)
        {
            var visual = actor.GetComponentInChildren<SpriteRenderer>();
            var local = visual.sprite.bounds;
            var result = new Bounds(visual.transform.TransformPoint(local.min), Vector3.zero);
            result.Encapsulate(visual.transform.TransformPoint(local.max));
            result.Encapsulate(visual.transform.TransformPoint(new Vector3(local.min.x, local.max.y)));
            result.Encapsulate(visual.transform.TransformPoint(new Vector3(local.max.x, local.min.y)));
            return result;
        }

        WarningArea Warning(Vector2 position, Vector2 size)
        {
            var instance = Instantiate(warningPrefab, actors.transform);
            instance.name = "AttackWarning";
            instance.transform.position = position;
            var area = instance.GetComponent<WarningArea>();
            area.size = size;
            area.duration = 1000;
            area.Play();
            return area;
        }

        void Remove(Component item) { if (item != null) { item.gameObject.SetActive(false); Destroy(item.gameObject); } }
        Vector2 Player => game.player.transform.position;

        void Fire(string label, Vector2 origin, Vector2 direction, float speed, float size = .8f, float growth = 0)
        {
            var instance = new GameObject(label);
            instance.transform.SetParent(actors.transform, false);
            instance.transform.position = origin;
            var text = instance.AddComponent<TextMesh>();
            text.text = label;
            text.font = game.worldFont;
            text.fontSize = 80;
            text.characterSize = .15f;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = GameController.Accent;
            var render = instance.GetComponent<MeshRenderer>();
            render.sharedMaterial = game.worldFont.material;
            render.sortingOrder = 12;
            var bounds = render.bounds;
            float scale = size * textSizeMultiplier / Mathf.Max(.001f, bounds.size.y);
            var hit = instance.AddComponent<BoxCollider2D>();
            hit.size = new Vector2(Mathf.Max(.1f, bounds.size.x), Mathf.Max(.1f, bounds.size.y));
            hit.offset = instance.transform.InverseTransformPoint(bounds.center);
            hit.isTrigger = true;
            instance.AddComponent<Danger>().Configure(game);
            instance.transform.localScale = Vector3.one * scale;
            shots.Add(new Shot
            {
                target = instance.transform,
                velocity = direction.normalized * speed * (label == "L" ? 1.5f : 1),
                growth = growth,
                lifetime = label == "HEY" ? (width + 2 + hit.size.x * scale * .5f + .2f) / speed : 5
            });
        }

        void Hey(float speed = 22)
        {
            // Lock the player's current height. A fast horizontal pass, not homing after launch.
            bool fromRight = Player.x <= 0;
            Fire("HEY", new Vector2((fromRight ? 1 : -1) * (width * .5f + 2), Player.y),
                fromRight ? Vector2.left : Vector2.right, speed, .65f);
        }

        void Update()
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var shot = shots[i];
                if (shot.target == null) { shots.RemoveAt(i); continue; }
                shot.age += Time.deltaTime;
                shot.target.position += (Vector3)(shot.velocity * Time.deltaTime);
                if (shot.growth > 0) shot.target.localScale *= 1 + shot.growth * Time.deltaTime;
                if (shot.age > shot.lifetime || Mathf.Abs(shot.target.position.x) > width * .5f + 7
                    || shot.target.position.y < bottom - 5 || shot.target.position.y > top + 7)
                { Destroy(shot.target.gameObject); shots.RemoveAt(i); }
            }
        }

        IEnumerator Clip(AudioClip clip, Action<float> frame, bool hey = false, float extraTime = 1, int exactPlays = 0)
        {
            hey = hey && enableHeyAttacks;
            if (clip == lAudio && exactPlays == 0) exactPlays = 2;
            voice.loop = true;
            voice.clip = clip;
            voice.Play();
            if (exactPlays > 0) voice.SetScheduledEndTime(AudioSettings.dspTime + clip.length * exactPlays);
            bool fired = false;
            WarningArea heyWarning = null;
            bool fill = CurrentPanel == 1 || CurrentPanel == 3 || CurrentPanel == 5 || CurrentPanel == 7;
            float multiplier = exactPlays > 0 ? 1 : executionTimeMultiplier * extraTime;
            float duration = exactPlays > 0 ? clip.length * exactPlays
                : hey ? Mathf.Max(clip.length * multiplier, 3.05f) : clip.length * multiplier;
            for (float time = 0; time < duration; time += Time.deltaTime)
            {
                if (fill && voice.clip != lAudio && time >= clip.length)
                {
                    voice.clip = lAudio;
                    voice.loop = true;
                    voice.Play();
                }
                frame(time / multiplier);
                if (hey && !fired && time >= 2.25f)
                {
                    if (heyWarning == null)
                    {
                        heyWarning = Warning(new Vector2(0, Player.y), new Vector2(width, .65f * textSizeMultiplier));
                        heyWarning.name = "HEYWarning";
                    }
                    heyWarning.transform.position = new Vector2(0, Player.y);
                    if (time >= 2.8f)
                    {
                        Remove(heyWarning);
                        float speed = Mathf.Max(22, (width + 7) / Mathf.Max(.05f, duration - 2.8f - .05f));
                        Hey(speed); fired = true;
                    }
                }
                yield return null;
            }
            // Finish the full horizontal pass before this panel can clear its actors.
            while (shots.Exists(shot => shot.target != null && shot.target.name == "HEY")) yield return null;
            voice.Stop();
            voice.loop = false;
            Remove(heyWarning);
        }

        IEnumerator FixedRise(GameObject prefab, AudioClip clip, bool shake, int launchCount = 1, int audioPlays = 2)
        {
            voice.clip = clip;
            voice.loop = audioPlays > 1;
            voice.Play();
            float total = clip.length * audioPlays;
            float cycle = total / launchCount;
            float started = Time.time;
            for (int index = 0; index < launchCount; index++)
            {
                // Keep the warning/hold/flight ratio inside the requested number of audio plays.
                float lockedX = Player.x;
                float cycleStart = started + cycle * index;
                var warning = Warning(new Vector2(lockedX, bottom + 1.5f), new Vector2(3.5f, 3));
                while (Time.time < cycleStart + cycle / 3) yield return null;
                Remove(warning);
                while (Time.time < cycleStart + cycle * .5f) yield return null;
                var figure = Figure(prefab, shake ? "RisingCream" : "VegetableLaunch", new Vector2(lockedX, bottom), shake ? 9 * .7f * .8f : 9);
                var bounds = VisualBounds(figure);
                float startY = bottom - bounds.extents.y - .15f;
                float endY = top + bounds.extents.y + .2f;
                float flight = cycle * .5f;
                while (Time.time < cycleStart + cycle)
                {
                    float t = Mathf.Clamp01((Time.time - cycleStart - flight) / flight);
                    figure.position = new Vector2(lockedX + (shake ? Mathf.Sin(t * Mathf.PI * 8) * .25f : 0), Mathf.Lerp(startY, endY, t));
                    yield return null;
                }
                Remove(figure);
            }
            voice.Stop();
            voice.loop = false;
        }

        void ClearPanel()
        {
            foreach (Transform child in actors.transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            shots.Clear();
        }

        IEnumerator Perform()
        {
            yield return new WaitForSeconds(.7f);
            CurrentPanel = 1;
            var figure = Figure(lPrefab, "TopEmitter", new Vector2(0, top + height * .25f), height * .5f);
            var firstBounds = VisualBounds(figure);
            float visualOffsetY = firstBounds.center.y - figure.position.y;
            figure.position = new Vector2(0, top + firstBounds.extents.y + .2f - visualOffsetY);
            Vector2 start = figure.position;
            Vector2 destination = new(0, top - firstBounds.extents.y - .15f - visualOffsetY);
            float next = .2f;
            int volley = 0;
            yield return Clip(lAudio, time =>
            {
                figure.position = Vector2.Lerp(start, destination, Mathf.Clamp01(time / .65f));
                if (time < next) return;
                next += .65f;
                var origin = (Vector2)figure.GetComponentInChildren<SpriteRenderer>().bounds.max;
                origin.x = figure.position.x;
                origin.y = Mathf.Min(origin.y, top + .2f);
                float angleShift = Mathf.Sin(volley++ * 1.3f) * 10 * Mathf.Deg2Rad;
                for (int i = -2; i <= 2; i++)
                {
                    float angle = Mathf.Atan(i * .45f) + angleShift;
                    Fire("L", origin, new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)), 7);
                }
            }, true);
            ClearPanel();

            CurrentPanel = 2;
            WarningArea warning = null;
            yield return FixedRise(creamPrefab, creamAudio, true, 1, audioPlays: 1);
            ClearPanel();
            yield return new WaitForSeconds(.3f);

            CurrentPanel = 3;
            figure = Figure(lPrefab, "RightEmitter", new Vector2(width * .5f + 4, 0), height * .5f);
            start = figure.position; destination = new Vector2(width * .25f, 0); next = .5f;
            yield return Clip(lAudio, time =>
            {
                figure.position = Vector2.Lerp(start, destination, Mathf.Clamp01(time / .5f));
                if (time < next) return;
                next += .5f;
                var bounds = figure.GetComponentInChildren<SpriteRenderer>().bounds;
                Vector2 origin = new(bounds.center.x, bounds.max.y);
                Fire("L", origin, Player - origin, 8, .5f, .65f);
            }, true);
            ClearPanel();

            CurrentPanel = 4;
            var diagonalWarning = Warning(new Vector2(-width * .25f, bottom + .6f), new Vector2(width * .5f, 1.2f));
            figure = Figure(crossPrefab, "Cross", new Vector2(width * .5f + 2, top + 2), 3 * 1.3f);
            bool changed = false;
            yield return Clip(crossAudio, time =>
            {
                float part = crossAudio.length * .5f;
                bool second = time >= part;
                if (second && !changed) { changed = true; diagonalWarning.transform.position = new Vector2(width * .25f, bottom + .6f); }
                float local = second ? time - part : time;
                float t = Mathf.Clamp01((local - warningSeconds) / Mathf.Max(.1f, part - warningSeconds));
                diagonalWarning.gameObject.SetActive(local < warningSeconds);
                Vector2 a = new((second ? -1 : 1) * (width * .5f + 2), top + 2);
                Vector2 b = new((second ? 1 : -1) * (width * .5f + 2), bottom - 2);
                figure.position = Vector2.Lerp(a, b, t);
            });
            ClearPanel();

            if (enableSideLAttacks)
            {
                CurrentPanel = 5;
                figure = Figure(lPrefab, "SideEmitter", new Vector2(0, top - height * .25f), height * .5f);
                next = warningSeconds; int side = 0;
                warning = Warning(new Vector2(-width * .5f + .5f, bottom + .7f), new Vector2(1, 1.4f));
                yield return Clip(lAudio, time =>
                {
                    if (time < next) return;
                    bool fromLeft = side++ % 2 == 0;
                    Fire("L", new Vector2((fromLeft ? -1 : 1) * (width * .5f + 1), bottom + .65f), fromLeft ? Vector2.right : Vector2.left, 10);
                    next += .8f;
                    warning.transform.position = new Vector2((fromLeft ? 1 : -1) * (width * .5f - .5f), bottom + .7f);
                }, true);
                ClearPanel();
            }

            CurrentPanel = 6;
            yield return FixedRise(vegetablePrefab, vegetableAudio, false, audioPlays: 1);
            ClearPanel();
            yield return new WaitForSeconds(.3f);

            CurrentPanel = 7;
            var vertical = Figure(lPrefab, "VerticalL", Vector2.zero, 2.8f * 1.4f);
            var verticalBounds = VisualBounds(vertical);
            Vector2 verticalOffset = (Vector2)verticalBounds.center - (Vector2)vertical.position;
            float verticalY = bottom + verticalBounds.extents.y - verticalOffset.y;
            float verticalStartX = -width * .5f - verticalBounds.extents.x - .2f - verticalOffset.x;
            float verticalEndX = -verticalBounds.extents.x - verticalOffset.x;
            vertical.position = new Vector2(verticalStartX, verticalY);
            var horizontal = Figure(lPrefab, "HorizontalL", Vector2.zero, 2.8f * 1.4f);
            horizontal.rotation = Quaternion.Euler(0, 0, 90);
            var horizontalBounds = VisualBounds(horizontal);
            float horizontalOffsetY = horizontalBounds.center.y - horizontal.position.y;
            float horizontalStartY = bottom - horizontalBounds.extents.y - .2f - horizontalOffsetY;
            float colliderTop = float.NegativeInfinity;
            Physics2D.SyncTransforms();
            foreach (var hit in horizontal.GetComponentsInChildren<Collider2D>())
                colliderTop = Mathf.Max(colliderTop, hit.bounds.max.y);
            float horizontalEndY = float.IsNegativeInfinity(colliderTop)
                ? bottom - horizontalBounds.extents.y + 1.2f - horizontalOffsetY
                : bottom + 1.2f - colliderTop;
            horizontal.position = new Vector2(0, horizontalStartY);
            var leftWarning = Warning(new Vector2(-width * .5f + .6f, 0), new Vector2(1.2f, height));
            var floorWarning = Warning(new Vector2(0, bottom + .25f), new Vector2(width, .5f));
            yield return Clip(lAudio, time =>
            {
                if (time < .5f) return;
                leftWarning.gameObject.SetActive(false); floorWarning.gameObject.SetActive(false);
                float t = Mathf.Clamp01((time - .5f) / Mathf.Max(.1f, lAudio.length - .5f));
                vertical.position = new Vector2(Mathf.Lerp(verticalStartX, verticalEndX, t), verticalY);
                horizontal.position = new Vector2(0, Mathf.Lerp(horizontalStartY, horizontalEndY, t));
            }, exactPlays: 1);
            ClearPanel();

            CurrentPanel = 8;
            figure = Figure(selfiePrefab, "Selfie", new Vector2(0, 0), height * .7f);
            foreach (var hit in figure.GetComponentsInChildren<Collider2D>()) hit.enabled = false;
            var masks = SafeMasks();
            yield return Clip(selfieAudio, time =>
            {
                bool attacking = time >= Mathf.Min(warningSeconds, selfieAudio.length * .35f);
                foreach (var mask in masks)
                {
                    mask.GetComponent<BoxCollider2D>().enabled = attacking;
                    var render = mask.GetComponent<SpriteRenderer>();
                    var color = GameController.Accent; color.a = attacking ? .45f : .12f + .12f * Mathf.Abs(Mathf.Sin(time * 20));
                    render.color = color;
                }
            }, exactPlays: 1);
            ClearPanel(); Completed = true;
        }

        List<GameObject> SafeMasks()
        {
            float minX = -width * .5f, maxX = width * .5f;
            float minY = bottom, maxY = top;
            float left = safeCentre.x - safeSize.x * .5f, right = safeCentre.x + safeSize.x * .5f;
            float low = safeCentre.y - safeSize.y * .5f, high = safeCentre.y + safeSize.y * .5f;
            var result = new List<GameObject>();
            AddMask(result, new Vector2((minX + left) * .5f, (minY + maxY) * .5f), new Vector2(left - minX, maxY - minY));
            AddMask(result, new Vector2((right + maxX) * .5f, (minY + maxY) * .5f), new Vector2(maxX - right, maxY - minY));
            AddMask(result, new Vector2(safeCentre.x, (minY + low) * .5f), new Vector2(safeSize.x, low - minY));
            AddMask(result, new Vector2(safeCentre.x, (high + maxY) * .5f), new Vector2(safeSize.x, maxY - high));
            return result;
        }

        void AddMask(List<GameObject> list, Vector2 position, Vector2 size)
        {
            var instance = new GameObject("DamageOutsideSafeSquare");
            instance.transform.SetParent(actors.transform, false);
            instance.transform.position = position;
            instance.transform.localScale = new Vector3(size.x, size.y, 1);
            var render = instance.AddComponent<SpriteRenderer>();
            render.sprite = game.solidSprite;
            render.sortingOrder = 10;
            var hit = instance.AddComponent<BoxCollider2D>();
            hit.isTrigger = true; hit.enabled = false;
            instance.AddComponent<Danger>().Configure(game);
            list.Add(instance);
        }

        public void Stop()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (voice != null) { voice.Stop(); Destroy(voice); voice = null; }
            if (actors != null) { actors.SetActive(false); Destroy(actors); actors = null; }
            shots.Clear();
        }
        void OnDisable() => Stop();
    }
}
