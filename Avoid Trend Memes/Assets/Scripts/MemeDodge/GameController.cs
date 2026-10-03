using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using R3;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MemeDodge
{
    public sealed class GameController : MonoBehaviour
    {
        public static readonly Color Accent = new(223f / 255f, 1f, 6f / 255f);
        public GameObject titlePanel, hudPanel, rewardPanel;
        public Button startButton, healthPackButton;
        public Text healthText, stageText, timerText, messageText, titleStatus;
        public Slider progress;
        public PlayerMotor player;
        public PlayerInput input;
        public Transform playerArt, dangerRoot;
        public Sprite solidSprite;
        public Font worldFont;
        public Material lineMaterial;
        public AudioSource sound;
        GameSession session;
        GameAssets assets;
        readonly List<IDisposable> subscriptions = new();
        StageDefinition[] stages;
        CancellationTokenSource runCancellation;
        int lastStage = -1;
        bool starting;
        GameObject healthPickup;
        UnityEngine.UI.Text scoreText;
        Tween invulnerabilityBlink;
        Rigidbody2D playerBody;
        LineRenderer[] playerLines;
        SpriteRenderer[] playerSprites;
        public GameObject[] defaultPlatforms;
        GameObject stageLayout;

        [Inject] public void Construct(GameSession state, GameAssets resources) { session = state; assets = resources; }

        void Start()
        {
            playerBody = player.GetComponent<Rigidbody2D>();
            playerLines = playerArt.GetComponentsInChildren<LineRenderer>();
            playerSprites = playerArt.GetComponentsInChildren<SpriteRenderer>();
            scoreText = Instantiate(healthText, hudPanel.transform);
            scoreText.name = "Score";
            var scoreRect = scoreText.rectTransform;
            scoreRect.anchorMin = scoreRect.anchorMax = new Vector2(.5f, .93f);
            scoreRect.pivot = new Vector2(.5f, .5f);
            scoreRect.anchoredPosition = Vector2.zero;
            scoreRect.sizeDelta = new Vector2(280, 50);
            scoreText.fontSize = 26;
            scoreText.raycastTarget = false;
            subscriptions.Add(session.Score.Subscribe(value => scoreText.text = $"SCORE  {value}"));
            if (sound != null) { sound.Stop(); sound.playOnAwake = false; sound.mute = true; }
            subscriptions.Add(session.Health.Subscribe(value => healthText.text = $"HP  {new string('●', value)}{new string('○', GameSession.MaxHealth - value)}"));
            subscriptions.Add(session.Phase.Subscribe(ShowPhase));
            startButton.onClick.AddListener(() => StartRun().Forget(Debug.LogException));
            player.Jumped += OnJump;
            player.Landed += OnLand;
            player.enabled = false;
            player.gameObject.SetActive(false);
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
        }

        void LateUpdate()
        {
            if (session == null || !player.enabled) return;
            float tilt = Mathf.Clamp(-playerBody.linearVelocity.x * 1.4f, -9f, 9f);
            float angle = Mathf.LerpAngle(playerArt.localEulerAngles.z, tilt, 1 - Mathf.Exp(-12 * Time.deltaTime));
            playerArt.localRotation = Quaternion.Euler(0, 0, angle);
        }

        void SetPlayerOpacity(float alpha)
        {
            foreach (var line in playerLines)
            {
                var color = Accent; color.a = alpha;
                line.startColor = line.endColor = color;
            }
            foreach (var sprite in playerSprites)
            {
                var color = sprite.color; color.a = alpha; sprite.color = color;
            }
        }

        void ShowPhase(GamePhase phase)
        {
            titlePanel.SetActive(phase == GamePhase.Title || phase == GamePhase.Loading && starting);
            hudPanel.SetActive(phase != GamePhase.Title);
            rewardPanel.SetActive(false);
            startButton.interactable = phase == GamePhase.Title && !starting;
            bool canMove = phase == GamePhase.Playing || phase == GamePhase.Reward;
            player.enabled = canMove;
            input.enabled = canMove;
            if (!canMove) input.Clear();
            if (phase == GamePhase.GameOver) EndRun().Forget(Debug.LogException);
        }

        async UniTask StartRun()
        {
            if (starting || session.Phase.Value != GamePhase.Title) return;
            starting = true;
            runCancellation?.Dispose();
            runCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            var token = runCancellation.Token;
            session.NewRun();
            titleStatus.text = "밈 불러오는 중…";
            try
            {
                if (stages == null)
                {
                    var loadedStages = new StageDefinition[3];
                    for (int i = 0; i < loadedStages.Length; i++) loadedStages[i] = await assets.Load<StageDefinition>($"stage-{i}", token);
                    stages = loadedStages;
                }
                starting = false;
                lastStage = -1;
                player.gameObject.SetActive(true);
                await NextStage(token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                starting = false;
                session.Phase.Value = GamePhase.Title;
                titleStatus.text = "리소스 로딩 실패 — Console을 확인해주세요.";
                player.gameObject.SetActive(false);
                Debug.LogException(error);
            }
            finally { starting = false; }
        }

        async UniTask NextStage(CancellationToken token)
        {
            using var stageCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var stageToken = stageCancellation.Token;
            ClearDangers();
            bool firstStage = session.ClearedStages.Value == 0;
            if (firstStage)
            {
                player.transform.position = new Vector3(0, -2.7f, 0);
                playerBody.linearVelocity = Vector2.zero;
                playerArt.DOKill();
                playerArt.localScale = Vector3.one;
                playerArt.localRotation = Quaternion.identity;
                invulnerabilityBlink?.Kill();
                SetPlayerOpacity(1);
            }
            int chosen = UnityEngine.Random.Range(0, stages.Length - 1);
            if (lastStage >= 0 && chosen >= lastStage) chosen++;
            else if (lastStage < 0) chosen = UnityEngine.Random.Range(0, stages.Length);
            lastStage = chosen;
            var stage = stages[chosen];
            if (stageLayout != null) { stageLayout.SetActive(false); Destroy(stageLayout); }
            if (defaultPlatforms != null)
                foreach (var platform in defaultPlatforms) if (platform != null) platform.SetActive(stage.layoutPrefab == null);
            if (stage.layoutPrefab != null) stageLayout = Instantiate(stage.layoutPrefab, transform);
            stageText.text = $"STAGE {session.ClearedStages.Value + 1}  /  {stage.title}";
            messageText.text = stage.instruction;
            session.Phase.Value = GamePhase.Playing;
            float elapsed = 0;
            // Legacy automatic obstacles are disabled while stages are rebuilt.
            // float nextAttack = .8f;
            progress.value = 0;
            while (session.Phase.Value == GamePhase.Playing)
            {
                token.ThrowIfCancellationRequested();
                elapsed += Time.deltaTime;
                session.AdvanceScore(Time.deltaTime);
                progress.value = Mathf.Clamp01(elapsed / stage.duration);
                timerText.text = $"{Mathf.Max(0, Mathf.CeilToInt(stage.duration - elapsed))}s";
                if (elapsed >= stage.duration) break;
                // if (elapsed >= nextAttack)
                // {
                //     Attack(stage, stageToken).Forget(error => { if (error is not OperationCanceledException) Debug.LogException(error); });
                //     nextAttack += stage.attackInterval;
                // }
                if (player.transform.position.y < -5) HitPlayer();
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            stageCancellation.Cancel();
            if (session.ClearStage())
            {
                ClearDangers();
                messageText.text = "단계 클리어! 더블 점프로 힐팩을 먹으세요 (+1 HP)";
                SpawnHealthPack();
            }
        }

        async UniTask CollectPack()
        {
            if (!session.CollectHealthPack()) return;
            if (healthPickup != null) { healthPickup.transform.DOKill(); Destroy(healthPickup); healthPickup = null; }
            try { await NextStage(runCancellation.Token); }
            catch (OperationCanceledException) { }
        }

        void SpawnHealthPack()
        {
            // Floor surface is -3.3; use a margin below the ballistic double-jump apex.
            float standingCenter = -3.3f + player.GetComponent<BoxCollider2D>().size.y * .5f;
            float maxY = Mathf.Min(.15f, standingCenter + player.DoubleJumpHeight * .88f);
            float minY = Mathf.Min(-.55f, maxY);
            healthPickup = CreateBox("HealthPack", new Vector2(UnityEngine.Random.Range(-7f, 7f), UnityEngine.Random.Range(minY, maxY)), new Vector2(.6f, .6f), 1);
            healthPickup.transform.SetParent(transform, true);
            healthPickup.GetComponent<SpriteRenderer>().color = Color.black;
            AddWorldLabel(healthPickup.transform, "+", new Vector2(.6f, .6f));
            var trigger = healthPickup.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            healthPickup.AddComponent<HealthPickup>().Configure(this);
            var outline = healthPickup.AddComponent<LineRenderer>();
            outline.sharedMaterial = lineMaterial;
            outline.startColor = outline.endColor = Accent;
            outline.startWidth = outline.endWidth = .06f;
            outline.useWorldSpace = false;
            outline.loop = true;
            outline.positionCount = 4;
            outline.SetPositions(new[] { new Vector3(-.5f,-.5f), new Vector3(-.5f,.5f), new Vector3(.5f,.5f), new Vector3(.5f,-.5f) });
            healthPickup.transform.DOScale(.72f, .5f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(healthPickup);
        }

        public void TryCollectHealthPack() => CollectPack().Forget(Debug.LogException);

        /* Legacy obstacle patterns: falling memes, horizontal memes, warnings and lasers.
           Kept as reference; new stages will supply their own obstacle choreography.
        async UniTask Attack(StageDefinition stage, CancellationToken token)
        {
            bool laser = stage.pattern == MemePattern.BringTheL && UnityEngine.Random.value < .35f;
            Vector2 position;
            Vector2 size;
            Vector2 destination;
            string label;
            switch (stage.pattern)
            {
                case MemePattern.SixSeven:
                    position = new Vector2(Mathf.Clamp(player.transform.position.x + UnityEngine.Random.Range(-1.5f, 1.5f), -7.2f, 7.2f), 4.7f);
                    size = new Vector2(.9f, .9f);
                    destination = new Vector2(position.x, -4.7f);
                    label = UnityEngine.Random.value > .5f ? "6" : "7";
                    break;
                case MemePattern.BringTheL:
                    position = new Vector2(8.8f, -2.35f);
                    size = new Vector2(1.3f, 1.4f);
                    destination = new Vector2(-9, position.y);
                    label = "L";
                    break;
                default:
                    position = new Vector2(8.8f, UnityEngine.Random.value > .5f ? -2.5f : -.5f);
                    size = new Vector2(2.2f, .8f);
                    destination = new Vector2(-9, position.y);
                    string[] phrases = { "아니 근데", "그게 맞냐", "내 말은" };
                    label = phrases[UnityEngine.Random.Range(0, phrases.Length)];
                    break;
            }
            if (laser) { position = stage.laserPosition; size = stage.laserSize; destination = position; label = ""; }
            var warning = CreateBox("Warning", laser ? position : stage.pattern == MemePattern.SixSeven ? new Vector2(position.x, -1) : new Vector2(6.6f, position.y), laser ? new Vector2(16, .12f) : new Vector2(.12f, 4f), .25f);
            warning.transform.DOScaleX(.5f, .15f).SetLoops(-1, LoopType.Yoyo).SetLink(warning);
            await UniTask.Delay(TimeSpan.FromSeconds(stage.warningDuration), cancellationToken: token);
            if (warning != null) Destroy(warning);
            if (session.Phase.Value != GamePhase.Playing || token.IsCancellationRequested) return;
            bool useImage = !laser && stage.memePrefabs != null && stage.memePrefabs.Length > 0;
            GameObject danger;
            if (useImage)
            {
                var prefab = stage.memePrefabs[UnityEngine.Random.Range(0, stage.memePrefabs.Length)];
                danger = Instantiate(prefab, dangerRoot);
                danger.transform.position = position;
                // Image prefabs have a baked 1.4-unit height. Preserve their aspect ratio.
                danger.transform.localScale = Vector3.one * (size.y / 1.4f);
                danger.GetComponent<Danger>().Configure(this);
            }
            else
            {
                danger = CreateBox(label, position, size, 1f);
                var render = danger.GetComponent<SpriteRenderer>();
                render.color = Color.black;
                var outline = danger.AddComponent<LineRenderer>();
                outline.sharedMaterial = lineMaterial;
                outline.startColor = outline.endColor = Accent;
                outline.startWidth = outline.endWidth = .055f;
                outline.useWorldSpace = false;
                outline.loop = true;
                outline.positionCount = 4;
                outline.SetPositions(new[] { new Vector3(-.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f),new Vector3(.5f,-.5f) });
                if (stage.pattern == MemePattern.BringTheL && !laser)
                {
                    outline.positionCount = 6;
                    outline.SetPositions(new[] { new Vector3(-.5f,-.5f), new Vector3(-.5f,.5f), new Vector3(-.2f,.5f), new Vector3(-.2f,-.2f), new Vector3(.5f,-.2f), new Vector3(.5f,-.5f) });
                }
                if (stage.pattern == MemePattern.BringTheL && !laser)
                {
                    var polygon = danger.AddComponent<PolygonCollider2D>();
                    polygon.points = new[] { new Vector2(-.5f,-.5f), new Vector2(-.5f,.5f), new Vector2(-.2f,.5f), new Vector2(-.2f,-.2f), new Vector2(.5f,-.2f), new Vector2(.5f,-.5f) };
                    polygon.isTrigger = true;
                }
                else danger.AddComponent<BoxCollider2D>().isTrigger = true;
                danger.AddComponent<Danger>().Configure(this);
                if (!string.IsNullOrEmpty(label)) AddWorldLabel(danger.transform, label, size);
                if (laser) render.color = Accent;
            }
            float duration = laser ? .55f : Vector2.Distance(position, destination) / stage.projectileSpeed;
            danger.transform.DOMove(destination, duration).SetEase(Ease.Linear).SetLink(danger).OnComplete(() => Destroy(danger));
        }

        */

        GameObject CreateBox(string name, Vector2 position, Vector2 size, float alpha)
        {
            var go = new GameObject(name);
            go.transform.SetParent(dangerRoot);
            go.transform.position = position;
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = solidSprite;
            renderer.color = new Color(Accent.r, Accent.g, Accent.b, alpha);
            renderer.sortingOrder = 5;
            return go;
        }

        void AddWorldLabel(Transform parent, string label, Vector2 size)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(1 / size.x, 1 / size.y, 1);
            var text = go.AddComponent<TextMesh>();
            text.text = label;
            text.font = worldFont;
            text.fontSize = 64;
            text.characterSize = label.Length > 1 ? .09f : .2f;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = Accent;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = worldFont.material;
            renderer.sortingOrder = 7;
        }

        public void HitPlayer()
        {
            if (!session.Damage(Time.time)) return;
            playerArt.DOKill();
            playerArt.localScale = Vector3.one;
            playerArt.DOPunchScale(new Vector3(.15f, .15f, 0), .25f, 6).SetLink(playerArt.gameObject);
            Camera.main.transform.DOShakePosition(.2f, .15f, 12).SetLink(Camera.main.gameObject);
            invulnerabilityBlink?.Kill();
            invulnerabilityBlink = DOTween.To(() => 1f, SetPlayerOpacity, .2f, .1f)
                .SetLoops(10, LoopType.Yoyo).SetEase(Ease.Linear).SetLink(playerArt.gameObject)
                .OnComplete(() => SetPlayerOpacity(1));
        }

        void OnJump()
        {
            playerArt.DOKill();
            playerArt.localScale = Vector3.one;
            playerArt.DOPunchScale(new Vector3(.12f, .12f, 0), .3f, 4).SetLink(playerArt.gameObject);
        }

        void OnLand()
        {
            playerArt.DOKill();
            playerArt.localScale = Vector3.one;
            playerArt.DOPunchScale(new Vector3(.08f, .08f, 0), .22f, 4).SetLink(playerArt.gameObject);
        }

        async UniTask EndRun()
        {
            runCancellation?.Cancel();
            ClearDangers();
            string result = $"{GameSession.RankForScore(session.Score.Value)}   {session.Score.Value}점";
            messageText.text = $"GAME OVER   |   {result}";
            await UniTask.Delay(TimeSpan.FromSeconds(2.5f), cancellationToken: destroyCancellationToken);
            player.gameObject.SetActive(false);
            titleStatus.text = $"{result}   /   {session.ClearedStages.Value}단계 클리어";
            session.Phase.Value = GamePhase.Title;
        }
        void ClearDangers()
        {
            foreach (Transform child in dangerRoot)
            {
                child.gameObject.SetActive(false);
                child.DOKill();
                Destroy(child.gameObject);
            }
        }
        void OnDestroy()
        {
            runCancellation?.Cancel();
            runCancellation?.Dispose();
            if (player != null) player.Jumped -= OnJump;
            if (player != null) player.Landed -= OnLand;
            invulnerabilityBlink?.Kill();
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }
}
