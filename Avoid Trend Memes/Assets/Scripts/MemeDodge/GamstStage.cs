using System.Collections;
using UnityEngine;

namespace MemeDodge
{
    // Read the storyboard left-to-right, then continue on the next row.
    public sealed class GamstStage : MonoBehaviour
    {
        public GameObject treePrefab, bicyclePrefab, warningPrefab, gamstPrefab, injikPrefab;
        public AudioClip introduction, direction, thanks, departure, dal, funny, veryFunny;
        [Min(1)] public float treeScale = 4.5f;
        [Min(.1f)] public float warningSeconds = .55f;
        [Min(0)] public float breathingSeconds = .3f;
        AudioSource voice;
        Transform left, right, bicycle;
        Coroutine routine;
        GameObject actors;
        GameController controller;
        public int CurrentPanel { get; private set; }
        public bool Completed { get; private set; }
        public float Duration => 2.2f + warningSeconds * 8 + Mathf.Max(warningSeconds, Length(departure)) + breathingSeconds * 8
            + Length(introduction) + Length(direction) * 2 + Length(thanks)
            + Length(dal) * 3 + Length(funny) + Length(veryFunny);
        static float Length(AudioClip clip) => clip == null ? 0 : clip.length;

        public void Begin(GameController game)
        {
            Stop();
            Completed = false;
            controller = game;
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0;
            actors = new GameObject("GamstStoryboardActors");
            actors.transform.SetParent(transform, false);
            left = Spawn(treePrefab, "LeftTree", new Vector2(-5.8f, -8), treeScale, game);
            right = Spawn(treePrefab, "RightTree", new Vector2(5.8f, -8), treeScale, game);
            bicycle = Spawn(bicyclePrefab, "Bicycle", new Vector2(11, -2.15f), 1.5f, game);
            routine = StartCoroutine(Perform());
        }

        Transform Spawn(GameObject prefab, string label, Vector2 position, float scale, GameController game)
        {
            var instance = Instantiate(prefab, actors.transform);
            instance.name = label;
            instance.transform.position = position;
            instance.transform.localScale = Vector3.one * scale;
            foreach (var danger in instance.GetComponentsInChildren<Danger>(true)) danger.Configure(game);
            return instance.transform;
        }

        void FitHeight(Transform actor, float height)
        {
            var visual = actor.GetComponentInChildren<SpriteRenderer>();
            actor.localScale *= height / Mathf.Max(.001f, visual.bounds.size.y);
            Physics2D.SyncTransforms();
        }

        Vector2 VisibleSize(Transform actor) => actor.GetComponentInChildren<SpriteRenderer>().bounds.size;

        IEnumerator Warn(Vector2 position, Vector2 size, bool mirror = false, AudioClip cue = null)
        {
            float duration = cue == null ? warningSeconds : Mathf.Max(warningSeconds, cue.length);
            if (cue != null) { voice.clip = cue; voice.Play(); }
            var instance = Instantiate(warningPrefab, actors.transform);
            instance.transform.position = position;
            var area = instance.GetComponent<WarningArea>();
            area.size = size;
            area.duration = duration;
            area.Play();
            GameObject second = null;
            if (mirror)
            {
                second = Instantiate(warningPrefab, actors.transform);
                second.transform.position = new Vector2(-position.x, position.y);
                var other = second.GetComponent<WarningArea>();
                other.size = size;
                other.duration = duration;
                other.Play();
            }
            yield return new WaitForSeconds(duration);
            if (cue != null) voice.Stop();
            Destroy(instance);
            if (second != null) Destroy(second);
        }

        IEnumerator Move(AudioClip clip, Transform a, Vector2 endA, Transform b = null, Vector2 endB = default, float bounce = 0)
        {
            var startA = (Vector2)a.position;
            var startB = b == null ? Vector2.zero : (Vector2)b.position;
            float duration = Mathf.Max(.01f, Length(clip));
            voice.clip = clip;
            voice.Play();
            float elapsed = 0;
            while (elapsed < duration)
            {
                // AudioSource.time follows the actual cut clip rather than a separately authored timeline.
                elapsed = voice.isPlaying ? voice.time : elapsed + Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                a.position = Vector2.Lerp(startA, endA, t);
                if (b != null) b.position = Vector2.Lerp(startB, endB, t);
                if (bounce > 0)
                {
                    // Stay level at both ends; make one jump while crossing the centre.
                    float jumpT = Mathf.Clamp01((t - .35f) / .3f);
                    var offset = Vector3.up * (4 * jumpT * (1 - jumpT) * bounce);
                    a.position += offset;
                    if (b != null) b.position += offset;
                }
                yield return null;
            }
            a.position = endA;
            if (b != null) b.position = endB;
            voice.Stop();
        }

        IEnumerator Perform()
        {
            yield return new WaitForSeconds(.7f);
            float treeY = -3.3f + treeScale * .7f;
            var treeWarning = new Vector2(2.8f, treeScale * 1.4f);
            // Leave a narrow centre gap even when the tree sprite size changes.
            float treeCentreX = Mathf.Max(left.GetComponentInChildren<Collider2D>().bounds.extents.x,
                right.GetComponentInChildren<Collider2D>().bounds.extents.x) + .45f;
            CurrentPanel = 1;
            // Panel 1: trees rise together; the middle remains available to the player.
            yield return Warn(new Vector2(-5.8f, treeY), treeWarning, true);
            yield return Move(introduction, left, new Vector2(-5.8f, treeY), right, new Vector2(5.8f, treeY));
            yield return new WaitForSeconds(breathingSeconds);
            // Panels 2–3 are two distinct direction lines, each showing a start and end pose.
            CurrentPanel = 2;
            yield return Warn(new Vector2(-treeCentreX, treeY), treeWarning);
            yield return Move(direction, left, new Vector2(-treeCentreX, treeY));
            yield return new WaitForSeconds(breathingSeconds);
            CurrentPanel = 3;
            yield return Warn(new Vector2(treeCentreX, treeY), treeWarning);
            yield return Move(direction, right, new Vector2(treeCentreX, treeY));
            yield return new WaitForSeconds(breathingSeconds);
            // Panel 4: the low bicycle crosses from the right; jumping clears it.
            CurrentPanel = 4;
            bicycle.localScale = new Vector3(-Mathf.Abs(bicycle.localScale.x), bicycle.localScale.y, bicycle.localScale.z);
            yield return Warn(new Vector2(0, -2.45f), new Vector2(16, 1.7f));
            yield return Move(thanks, bicycle, new Vector2(-11, -2.15f));
            left.gameObject.SetActive(false);
            right.gameObject.SetActive(false);
            yield return new WaitForSeconds(breathingSeconds);

            // Panel 5: announce the right-side entry before the first horizontal pass.
            float screenHeight = Camera.main.orthographicSize * 2;
            float halfHeight = screenHeight * .5f;
            float manY = -3.3f + halfHeight * .5f;
            var man = Spawn(gamstPrefab, "Gamst", new Vector2(12, manY), 1, controller);
            FitHeight(man, halfHeight);
            // The playable walls are at +/-8.4. Preserve two units at each side.
            float manHalfWidth = man.GetComponentInChildren<Collider2D>().bounds.extents.x;
            float stopX = Mathf.Max(0, 8.4f - 2f - manHalfWidth);
            CurrentPanel = 5;
            yield return Warn(new Vector2(5.2f, manY), VisibleSize(man), cue: departure);
            CurrentPanel = 6;
            yield return Move(dal, man, new Vector2(-stopX, manY));
            man.gameObject.SetActive(false);
            Destroy(man.gameObject);
            yield return new WaitForSeconds(breathingSeconds);
            CurrentPanel = 7;
            float leftEntry = Camera.main.transform.position.x - screenHeight * Camera.main.aspect * .5f - manHalfWidth - .2f;
            man = Spawn(gamstPrefab, "Gamst", new Vector2(leftEntry, manY), 1, controller);
            FitHeight(man, halfHeight);
            yield return Warn(new Vector2(-5.2f, manY), VisibleSize(man));
            yield return Move(dal, man, new Vector2(stopX, manY));
            man.gameObject.SetActive(false);
            yield return new WaitForSeconds(breathingSeconds);

            CurrentPanel = 8;
            var injik = Spawn(injikPrefab, "InjikImpact", new Vector2(3, screenHeight), 1, controller);
            FitHeight(injik, halfHeight);
            yield return Warn(new Vector2(3, manY), VisibleSize(injik));
            yield return Move(funny, injik, new Vector2(3, manY));
            yield return Shockwaves(3);
            injik.gameObject.SetActive(false);
            yield return new WaitForSeconds(breathingSeconds);

            CurrentPanel = 9;
            // Three times the previous one-third-height pair.
            float pairHeight = screenHeight;
            bicycle.localScale = new Vector3(Mathf.Abs(bicycle.localScale.x), bicycle.localScale.y, bicycle.localScale.z);
            man.gameObject.SetActive(true);
            FitHeight(bicycle, pairHeight * .5f);
            FitHeight(man, pairHeight * .5f);
            float bikeY = -3.3f + pairHeight * .25f - .8f;
            bicycle.position = new Vector2(12, bikeY);
            man.position = new Vector2(12, bikeY);
            Physics2D.SyncTransforms();
            var bikeBounds = bicycle.GetComponentInChildren<Collider2D>().bounds;
            var riderBounds = man.GetComponentInChildren<Collider2D>().bounds;
            float riderY = bikeY + bikeBounds.max.y - riderBounds.min.y - .6f;
            man.position = new Vector2(12, riderY);
            Physics2D.SyncTransforms();
            yield return Warn(new Vector2(0, -3.3f + pairHeight * .5f), new Vector2(16, pairHeight));
            yield return Move(dal, bicycle, new Vector2(-12, bikeY), man, new Vector2(-12, riderY), 2.2f);
            man.gameObject.SetActive(false);
            yield return new WaitForSeconds(breathingSeconds);

            CurrentPanel = 10;
            injik.gameObject.SetActive(true);
            FitHeight(injik, screenHeight);
            var fullSize = VisibleSize(injik);
            float screenWidth = screenHeight * Camera.main.aspect;
            if (fullSize.x < screenWidth) injik.localScale *= screenWidth / fullSize.x;
            fullSize = VisibleSize(injik);
            var cameraCenter = (Vector2)Camera.main.transform.position;
            injik.position = new Vector2(cameraCenter.x, cameraCenter.y - screenHeight * .5f - fullSize.y * .5f - .2f);
            yield return Warn(cameraCenter, new Vector2(screenWidth, screenHeight));
            yield return Move(veryFunny, injik, cameraCenter);
            yield return new WaitForSeconds(.3f);
            Completed = true;
        }

        IEnumerator Shockwaves(float impactX)
        {
            var waves = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var wave = new GameObject(i == 0 ? "LeftShockwave" : "RightShockwave");
                wave.transform.SetParent(actors.transform, false);
                wave.transform.localScale = Vector3.one * 2;
                wave.transform.position = new Vector2(impactX, -2.98f);
                var line = wave.AddComponent<LineRenderer>();
                line.sharedMaterial = controller.lineMaterial;
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = 32;
                line.startWidth = line.endWidth = .13f;
                line.startColor = line.endColor = GameController.Accent;
                line.sortingOrder = 8;
                for (int p = 0; p < 32; p++)
                {
                    float angle = p * Mathf.PI * 2 / 32;
                    line.SetPosition(p, new Vector3(Mathf.Cos(angle) * .65f, Mathf.Sin(angle) * .16f));
                }
                var hit = wave.AddComponent<BoxCollider2D>();
                hit.size = new Vector2(1.3f, .32f);
                hit.isTrigger = true;
                wave.AddComponent<Danger>().Configure(controller);
                waves[i] = wave.transform;
            }
            for (float time = 0; time < 1.2f; time += Time.deltaTime)
            {
                for (int i = 0; i < 2; i++)
                    waves[i].position = new Vector2(impactX + (i == 0 ? -1 : 1) * time * 12, -2.98f);
                yield return null;
            }
            foreach (var wave in waves) Destroy(wave.gameObject);
        }

        public void Stop()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (voice != null) { voice.Stop(); Destroy(voice); voice = null; }
            if (actors != null) { actors.SetActive(false); Destroy(actors); actors = null; }
        }
        void OnDisable() => Stop();
    }
}
