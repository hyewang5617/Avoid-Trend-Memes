using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MemeDodge
{
    public sealed class GeojeStage : MonoBehaviour
    {
        public GameObject portraitPrefab, fullPhrasePrefab, geojePrefab, yahoPrefab, warningPrefab;
        public AudioClip introAudio, citizensAudio, danceAudio, fullPhraseAudio, geojeAudio, yahoAudio;
        public float remainingSpace = 2.5f;
        public int CurrentPanel { get; private set; }
        public bool Completed { get; private set; }
        const int RepeatsPerPhrase = 9;
        float DanceDuration => danceAudio.length;
        public float Duration => introAudio.length + citizensAudio.length * 4 + DanceDuration;
        public IReadOnlyList<int> PatternOrder => patterns;
        readonly List<int> patterns = new();
        GameController game;
        GameObject actors;
        AudioSource voice, music;
        Coroutine routine;
        int activePhrases;
        float left, right, top;
        const float Ground = -3.3f;

        public void Begin(GameController controller)
        {
            Stop(); game = controller; Completed = false; activePhrases = 0;
            var camera = Camera.main;
            top = camera.transform.position.y + camera.orthographicSize;
            left = camera.transform.position.x - camera.orthographicSize * camera.aspect;
            right = camera.transform.position.x + camera.orthographicSize * camera.aspect;
            actors = new GameObject("GeojeStoryboardActors");
            actors.transform.SetParent(transform, false);
            voice = gameObject.AddComponent<AudioSource>(); voice.playOnAwake = false;
            music = gameObject.AddComponent<AudioSource>(); music.playOnAwake = false;
            patterns.Clear();
            for (int i = 0; i < RepeatsPerPhrase; i++) { patterns.Add(4); patterns.Add(5); }
            for (int i = patterns.Count - 1; i > 0; i--)
            { int j = Random.Range(0, i + 1); (patterns[i], patterns[j]) = (patterns[j], patterns[i]); }
            routine = StartCoroutine(Perform());
        }

        Transform Platform(string label, Vector2 position, Vector2 size, bool oneWay)
        {
            var item = new GameObject(label); item.transform.SetParent(actors.transform, false);
            item.transform.position = position; item.transform.localScale = new Vector3(size.x, size.y, 1);
            item.layer = 8;
            var render = item.AddComponent<SpriteRenderer>(); render.sprite = game.solidSprite;
            render.color = GameController.Accent; render.sortingOrder = 5;
            item.AddComponent<BoxCollider2D>();
            if (oneWay) { item.AddComponent<PlatformEffector2D>(); item.AddComponent<OneWayPlatform>(); }
            return item.transform;
        }

        Transform Figure(GameObject prefab, string label, Vector2 position, float height, bool harmful)
        {
            var item = Instantiate(prefab, actors.transform); item.name = label; item.transform.position = position;
            var render = item.GetComponentInChildren<SpriteRenderer>();
            var bounds = render.sprite.bounds;
            float size = Vector3.Distance(render.transform.TransformPoint(new Vector3(0, bounds.min.y)),
                render.transform.TransformPoint(new Vector3(0, bounds.max.y)));
            item.transform.localScale *= height / Mathf.Max(.001f, size);
            render.sortingOrder = harmful ? 7 : 0;
            foreach (var collider in item.GetComponentsInChildren<Collider2D>(true)) collider.enabled = harmful;
            foreach (var danger in item.GetComponentsInChildren<Danger>(true)) danger.Configure(game);
            return item.transform;
        }

        Transform Triangle(Vector2 position, bool toRight)
        {
            var item = new GameObject("GuidedTriangle"); item.transform.SetParent(actors.transform, false);
            item.transform.position = position;
            float sign = toRight ? 1 : -1;
            Vector2[] points = { new(sign * .55f, 0), new(-sign * .55f, .5f), new(-sign * .55f, -.5f) };
            var mesh = new Mesh(); mesh.vertices = new[] { (Vector3)points[0], (Vector3)points[1], (Vector3)points[2] };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 0 }; mesh.RecalculateBounds();
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var render = item.AddComponent<MeshRenderer>();
            render.material = game.lineMaterial; render.material.color = GameController.Accent; render.sortingOrder = 7;
            var collider = item.AddComponent<PolygonCollider2D>(); collider.points = points; collider.isTrigger = true;
            item.AddComponent<Danger>().Configure(game);
            StartCoroutine(CleanTriangle(item, mesh, render.material));
            return item.transform;
        }

        IEnumerator CleanTriangle(GameObject item, Mesh mesh, Material material)
        { while (item != null && item.activeInHierarchy) yield return null; Destroy(mesh); Destroy(material); }

        static void Remove(Transform item)
        { if (item != null) { item.gameObject.SetActive(false); Destroy(item.gameObject); } }

        Transform Warning(Vector2 position, Vector2 size, float duration)
        {
            var item = Instantiate(warningPrefab, actors.transform);
            item.name = "GeojeWarning"; item.transform.position = position;
            var warning = item.GetComponent<WarningArea>();
            warning.size = size; warning.duration = duration; warning.Play();
            return item.transform;
        }

        IEnumerator Perform()
        {
            CurrentPanel = 1;
            float introDuration = introAudio.length + citizensAudio.length * 4;
            var wall = Platform("ApproachingWall", new Vector2(left - .4f, (Ground + top) * .5f), new Vector2(.8f, top - Ground), false);
            var wallWarning = Warning(new Vector2(left + .4f, (Ground + top) * .5f), new Vector2(.8f, top - Ground), .6f);
            voice.clip = introAudio; voice.Play();
            music.clip = citizensAudio; music.loop = true;
            music.PlayScheduled(AudioSettings.dspTime + introAudio.length);
            float start = Time.time;
            StartCoroutine(GuidedTriangle(1.65f, .35f, introAudio.length - 2, true));
            float shotInterval = citizensAudio.length / 3;
            for (int i = 0; i < 4; i++)
                for (int shotIndex = 0; shotIndex < 3; shotIndex++)
                    StartCoroutine(GuidedTriangle(introAudio.length + i * citizensAudio.length + shotIndex * shotInterval,
                        .25f, shotInterval - .25f, false));
            while (Time.time - start < introDuration)
            {
                wall.position = new Vector2(Mathf.Lerp(left - .4f, right - remainingSpace - .4f,
                    Mathf.Clamp01((Time.time - start - .6f) / (introDuration - .6f))), wall.position.y);
                if (Time.time - start >= .6f) Remove(wallWarning);
                yield return null;
            }
            Remove(wallWarning); Remove(wall); voice.Stop(); music.Stop(); music.loop = false;
            CurrentPanel = 2;
            music.clip = danceAudio; music.loop = false; music.Play();
            float danceStart = Time.time;
            float portraitWidth = (right - left) * 2 / 3;
            float portraitX = right - portraitWidth * .5f - 2;
            float portraitStartX = right + portraitWidth * .5f;
            var portrait = Figure(portraitPrefab, "MinamiPortrait", new Vector2(portraitStartX, (Ground + top) * .5f), top - Ground, false);
            var portraitVisual = portrait.GetComponentInChildren<SpriteRenderer>();
            var spriteBounds = portraitVisual.sprite.bounds;
            float visualWidth = Vector3.Distance(portraitVisual.transform.TransformPoint(new Vector3(spriteBounds.min.x, 0)),
                portraitVisual.transform.TransformPoint(new Vector3(spriteBounds.max.x, 0)));
            var portraitScale = portrait.localScale; portraitScale.x *= portraitWidth / visualWidth; portrait.localScale = portraitScale;
            var lower = Platform("LowerPlatform", new Vector2(left + 2.4f, Ground - 1), new Vector2(3, .2f), true);
            var upper = Platform("UpperPlatform", new Vector2(left + 3.8f, Ground - 1), new Vector2(3, .2f), true);
            var lowerWarning = Warning(new Vector2(left + 2.4f, -1.5f), new Vector2(3, .4f), .6f);
            var upperWarning = Warning(new Vector2(left + 3.8f, .3f), new Vector2(3, .4f), .6f);
            var portraitWarning = Warning(new Vector2(portraitX, (Ground + top) * .5f), new Vector2(portraitWidth, top - Ground), .6f);
            while (Time.time - danceStart < 3)
            {
                float portraitT = Mathf.Clamp01((Time.time - danceStart) / 3);
                portrait.position = new Vector2(Mathf.Lerp(portraitStartX, portraitX, portraitT), (Ground + top) * .5f);
                float t = Mathf.SmoothStep(0, 1, (Time.time - danceStart - .6f) / 2.4f);
                if (Time.time - danceStart >= .6f)
                { Remove(lowerWarning); Remove(upperWarning); Remove(portraitWarning); }
                lower.position = new Vector2(left + 2.4f, Mathf.Lerp(Ground - 1, -1.5f, t));
                upper.position = new Vector2(left + 3.8f, Mathf.Lerp(Ground - 1, .3f, t));
                yield return null;
            }
            portrait.position = new Vector2(portraitX, (Ground + top) * .5f);
            yield return Phrase(3, fullPhrasePrefab, fullPhraseAudio);
            float firstAttack = Time.time;
            float attackWindow = Mathf.Max(0, danceStart + DanceDuration - firstAttack - 2);
            for (int i = 0; i < patterns.Count; i++)
            {
                // Early gaps are longer; later gaps shrink while the final shot still fits the music.
                float t = i / (float)(patterns.Count - 1);
                float offset = attackWindow * (t + .6f * t * (1 - t));
                while (Time.time < firstAttack + offset) yield return null;
                int panel = patterns[i];
                StartCoroutine(Phrase(panel, panel == 4 ? geojePrefab : yahoPrefab, panel == 4 ? geojeAudio : yahoAudio));
            }
            while (Time.time - danceStart < DanceDuration) yield return null;
            while (activePhrases > 0) yield return null;
            voice.Stop(); music.Stop(); Completed = true;
        }

        IEnumerator GuidedTriangle(float delay, float warningSeconds, float travelSeconds, bool toRight)
        {
            yield return new WaitForSeconds(delay);
            var warning = Warning(new Vector2((left + right) * .5f, game.player.transform.position.y),
                new Vector2(right - left, 1), warningSeconds);
            float start = Time.time;
            float y = game.player.transform.position.y;
            while (Time.time - start < warningSeconds)
            {
                y = Mathf.Clamp(game.player.transform.position.y, Ground + .5f, top - .5f);
                warning.position = new Vector2((left + right) * .5f, y);
                yield return null;
            }
            Remove(warning);
            Vector2 from = new(toRight ? left - .6f : right + .6f, y);
            Vector2 to = new(toRight ? right + .6f : left - .6f, y);
            var shot = Triangle(from, toRight);
            start = Time.time;
            while (Time.time - start < travelSeconds)
            {
                shot.position = Vector2.Lerp(from, to, Mathf.Clamp01((Time.time - start) / travelSeconds));
                yield return null;
            }
            Remove(shot);
        }

        IEnumerator Phrase(int panel, GameObject prefab, AudioClip clip)
        {
            activePhrases++;
            CurrentPanel = panel;
            voice.PlayOneShot(clip);
            var warningObject = Instantiate(warningPrefab, actors.transform);
            var warning = warningObject.GetComponent<WarningArea>();
            warning.size = new Vector2(right - left, .85f); warning.duration = 1; warning.Play();
            float start = Time.time;
            float y = game.player.transform.position.y;
            while (Time.time - start < 1)
            {
                y = Mathf.Clamp(game.player.transform.position.y, Ground + .5f, top - .5f);
                warningObject.transform.position = new Vector2((left + right) * .5f, y);
                yield return null;
            }
            Remove(warningObject.transform);
            var item = Figure(prefab, panel == 3 ? "GeojeYahoShot" : panel == 4 ? "GeojeShot" : "YahoShot",
                new Vector2(right, y), warning.size.y * 4.5f, true);
            var render = item.GetComponentInChildren<SpriteRenderer>();
            float halfWidth = render.bounds.extents.x;
            Vector2 from = new(right + halfWidth, y), to = new(left - halfWidth, y);
            while (Time.time - start < 2)
            {
                item.position = Vector2.Lerp(from, to, Mathf.Clamp01(Time.time - start - 1));
                yield return null;
            }
            Remove(item);
            while (Time.time - start < Mathf.Max(2, clip.length)) yield return null;
            activePhrases--;
        }

        public void Stop()
        {
            StopAllCoroutines(); routine = null;
            if (voice != null) { voice.Stop(); Destroy(voice); voice = null; }
            if (music != null) { music.Stop(); Destroy(music); music = null; }
            if (actors != null)
            {
                foreach (var filter in actors.GetComponentsInChildren<MeshFilter>())
                    if (filter.name == "GuidedTriangle") { Destroy(filter.sharedMesh); Destroy(filter.GetComponent<MeshRenderer>().sharedMaterial); }
                actors.SetActive(false); Destroy(actors); actors = null;
            }
        }
        void OnDisable() => Stop();
    }
}
