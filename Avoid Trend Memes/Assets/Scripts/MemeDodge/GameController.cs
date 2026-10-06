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
        public AudioClip menuMusic;
        AudioSource menuMusicSource;
        [Header("Stage testing")]
        [Tooltip("-1: normal progression. 0: Gamst. Non-negative numbers repeat only that stage.")]
        public int testStageNumber = 0;
        public bool enableStageTesting = false;
        [Tooltip("Below this world Y, lose one HP and return to a safe surface.")]
        public float fallY = -5;
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
        GameObject gameOverPanel;
        UnityEngine.UI.Text resultRank, resultScore;
        int gameOverFrame;
        GameObject modePanel;
        GameObject practicePanel;
        enum RunMode { Normal, Infinite, Practice }
        RunMode runMode;
        int practiceStage;
        readonly List<UnityEngine.UI.Button> modeButtons = new();
        readonly List<UnityEngine.UI.Button> practiceButtons = new();
        readonly List<UnityEngine.UI.Button> titleButtons = new();
        int modeSelection, practiceSelection, titleSelection;
        UnityEngine.UI.Toggle developerToggle;

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
            BuildGameOverUI();
            BuildModeUI();
            BuildDeveloperToggle();
            BuildTitleUI();
            menuMusicSource = gameObject.AddComponent<AudioSource>();
            menuMusicSource.playOnAwake = false; menuMusicSource.loop = true;
            menuMusicSource.spatialBlend = 0; menuMusicSource.clip = menuMusic;
            subscriptions.Add(session.Score.Subscribe(value => scoreText.text = $"SCORE  {value}"));
            if (sound != null) { sound.Stop(); sound.playOnAwake = false; sound.mute = true; }
            subscriptions.Add(session.Health.Subscribe(value => healthText.text = $"HP  {new string('●', value)}{new string('○', GameSession.MaxHealth - value)}"));
            subscriptions.Add(session.Phase.Subscribe(ShowPhase));
            startButton.onClick.AddListener(ShowModeSelection);
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
            if ((session.Phase.Value == GamePhase.Playing || session.Phase.Value == GamePhase.Reward)
                && player.transform.position.y < fallY)
            {
                var sixSeven = stageLayout == null ? null : stageLayout.GetComponent<SixSevenStage>();
                if (sixSeven != null && !sixSeven.Completed) sixSeven.RecoverToPlatform();
                else
                {
                    float halfHeight = player.GetComponent<BoxCollider2D>().bounds.extents.y;
                    var position = new Vector2(Mathf.Clamp(player.transform.position.x, -7, 7), -3.3f + halfHeight + .08f);
                    playerBody.position = position;
                    player.transform.position = position;
                    player.ResetAfterTeleport();
                    Physics2D.SyncTransforms();
                }
                HitPlayer(true);
            }
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
            bool menuVisible = phase != GamePhase.Playing && phase != GamePhase.Reward;
            if (menuMusicSource != null && menuMusic != null)
            {
                if (menuVisible && !menuMusicSource.isPlaying) menuMusicSource.Play();
                else if (!menuVisible && menuMusicSource.isPlaying) menuMusicSource.Stop();
            }
            titlePanel.SetActive(phase == GamePhase.Title || phase == GamePhase.Loading && starting);
            hudPanel.SetActive(phase != GamePhase.Title);
            rewardPanel.SetActive(false);
            startButton.interactable = phase == GamePhase.Title && !starting;
            if (phase == GamePhase.Title) SelectMenu(titleButtons, 0);
            bool canMove = phase == GamePhase.Playing || phase == GamePhase.Reward;
            player.enabled = canMove;
            input.enabled = canMove;
            if (!canMove) input.Clear();
            if (gameOverPanel != null) gameOverPanel.SetActive(phase == GamePhase.GameOver || phase == GamePhase.Victory);
            if (phase == GamePhase.GameOver) EndRun();
        }

        void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (session == null || !Application.isFocused || keyboard == null) return;
            if (keyboard.rightBracketKey.wasPressedThisFrame)
            {
                developerToggle.gameObject.SetActive(true);
                developerToggle.isOn = !developerToggle.isOn;
            }
            if ((session.Phase.Value == GamePhase.GameOver || session.Phase.Value == GamePhase.Victory)
                && Time.frameCount > gameOverFrame && keyboard.rKey.wasPressedThisFrame) ReturnToTitle();
            else if (session.Phase.Value == GamePhase.Title && !starting)
            {
                var buttons = practicePanel.activeSelf ? practiceButtons : modePanel.activeSelf ? modeButtons : titleButtons;
                if (buttons != null)
                {
                    int index = buttons == practiceButtons ? practiceSelection : buttons == modeButtons ? modeSelection : titleSelection;
                    int move = buttons == titleButtons
                        ? (keyboard.downArrowKey.wasPressedThisFrame ? 1 : 0) - (keyboard.upArrowKey.wasPressedThisFrame ? 1 : 0)
                        : (keyboard.rightArrowKey.wasPressedThisFrame ? 1 : 0) - (keyboard.leftArrowKey.wasPressedThisFrame ? 1 : 0);
                    if (move != 0) SelectMenu(buttons, (index + move + buttons.Count) % buttons.Count);
                    if (buttons == practiceButtons)
                    {
                        int vertical = (keyboard.upArrowKey.wasPressedThisFrame ? 1 : 0) - (keyboard.downArrowKey.wasPressedThisFrame ? 1 : 0);
                        if (vertical != 0) SelectPracticeRow(vertical);
                    }
                    if (keyboard.spaceKey.wasPressedThisFrame)
                    {
                        input.Clear();
                        index = buttons == practiceButtons ? practiceSelection : buttons == modeButtons ? modeSelection : titleSelection;
                        buttons[index].onClick.Invoke();
                    }
                }
                else if (keyboard.spaceKey.wasPressedThisFrame)
                {
                    input.Clear();
                    UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
                    ShowModeSelection();
                }
            }
        }

        void BuildGameOverUI()
        {
            gameOverPanel = new GameObject("GameOverPanel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            gameOverPanel.transform.SetParent(titlePanel.transform.parent, false);
            var rect = gameOverPanel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            gameOverPanel.GetComponent<UnityEngine.UI.Image>().color = Color.black;
            resultRank = ResultLabel("Rank", new Vector2(.28f, .5f), new Vector2(480, 440), 280);
            resultScore = ResultLabel("FinalScore", new Vector2(.68f, .65f), new Vector2(430, 100), 48);
            var buttonObject = new GameObject("ReturnToTitle", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            buttonObject.transform.SetParent(gameOverPanel.transform, false);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(.68f, .38f);
            buttonRect.sizeDelta = new Vector2(360, 90);
            buttonObject.GetComponent<UnityEngine.UI.Image>().color = Accent;
            var button = buttonObject.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = buttonObject.GetComponent<UnityEngine.UI.Image>();
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            button.onClick.AddListener(ReturnToTitle);
            var label = ResultLabel("ReturnLabel", Vector2.one * .5f, new Vector2(340, 80), 36);
            label.transform.SetParent(buttonObject.transform, false);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = Vector2.one * .5f;
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.text = "타이틀로  [R]"; label.color = Color.black;
            gameOverPanel.SetActive(false);
        }

        void BuildTitleUI()
        {
            titleButtons.Add(startButton);
            startButton.transition = UnityEngine.UI.Selectable.Transition.None;
            startButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            var startOutline = startButton.GetComponent<UnityEngine.UI.Outline>();
            if (startOutline == null) startOutline = startButton.gameObject.AddComponent<UnityEngine.UI.Outline>();
            startOutline.effectColor = Accent; startOutline.effectDistance = new Vector2(2, -2);
            var item = new GameObject("QuitGame", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            item.transform.SetParent(titlePanel.transform, false);
            var rect = item.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .19f);
            rect.sizeDelta = startButton.GetComponent<RectTransform>().sizeDelta * .7f;
            var image = item.GetComponent<UnityEngine.UI.Image>();
            var quit = item.GetComponent<UnityEngine.UI.Button>(); quit.targetGraphic = image;
            quit.transition = UnityEngine.UI.Selectable.Transition.None;
            quit.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            var outline = item.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = Accent; outline.effectDistance = new Vector2(2, -2);
            quit.onClick.AddListener(QuitGame); titleButtons.Add(quit);
            var label = ResultLabel("QuitLabel", Vector2.one * .5f, rect.sizeDelta - new Vector2(12, 12), 30);
            label.transform.SetParent(item.transform, false); label.text = "게임 종료";
            var credit = ResultLabel("CreatorCredit", new Vector2(.5f, .055f), new Vector2(500, 35), 20);
            credit.transform.SetParent(titlePanel.transform, false);
            credit.text = "made by hyewang5617";
            SelectMenu(titleButtons, 0);
        }

        void QuitGame()
        {
            SelectMenu(titleButtons, 1);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        UnityEngine.UI.Text ResultLabel(string label, Vector2 anchor, Vector2 size, int fontSize)
        {
            var text = Instantiate(healthText, gameOverPanel.transform);
            text.name = label; text.color = Accent; text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 20; text.resizeTextMaxSize = fontSize;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = anchor;
            text.rectTransform.anchoredPosition = Vector2.zero; text.rectTransform.sizeDelta = size;
            return text;
        }

        void ReturnToTitle()
        {
            if (session.Phase.Value != GamePhase.GameOver && session.Phase.Value != GamePhase.Victory) return;
            input.Clear();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            titleStatus.text = $"{GameSession.RankForScore(session.Score.Value)} / {session.Score.Value}점";
            session.Phase.Value = GamePhase.Title;
        }

        void BuildModeUI()
        {
            modePanel = new GameObject("ModeSelection", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            modePanel.transform.SetParent(titlePanel.transform.parent, false);
            var rect = modePanel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            modePanel.GetComponent<UnityEngine.UI.Image>().color = Color.black;
            var heading = ResultLabel("ChooseMode", new Vector2(.5f, .75f), new Vector2(600, 110), 52);
            heading.transform.SetParent(modePanel.transform, false); heading.text = "모드를 선택하세요";
            ModeButton("NormalMode", "일반 모드", new Vector2(.2f, .48f), modePanel.transform, () => BeginMode(RunMode.Normal));
            ModeButton("InfiniteMode", "무한 모드", new Vector2(.5f, .48f), modePanel.transform, () => BeginMode(RunMode.Infinite));
            ModeButton("PracticeMode", "연습 모드", new Vector2(.8f, .48f), modePanel.transform, () =>
            {
                modePanel.SetActive(false); practicePanel.SetActive(true); practicePanel.transform.SetAsLastSibling();
                SelectMenu(practiceButtons, 0);
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            });
            practicePanel = new GameObject("PracticeStageSelection", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            practicePanel.transform.SetParent(titlePanel.transform.parent, false);
            var practiceRect = practicePanel.GetComponent<RectTransform>();
            practiceRect.anchorMin = Vector2.zero; practiceRect.anchorMax = Vector2.one; practiceRect.sizeDelta = Vector2.zero;
            practicePanel.GetComponent<UnityEngine.UI.Image>().color = Color.black;
            var practiceHeading = ResultLabel("ChoosePracticeStage", new Vector2(.5f, .82f), new Vector2(650, 110), 48);
            practiceHeading.transform.SetParent(practicePanel.transform, false); practiceHeading.text = "연습할 스테이지를 선택하세요";
            string[] stageNames = { "감스트", "L을 가져가", "67", "거제야호", "줴줴이야" };
            for (int i = 0; i < stageNames.Length; i++)
            {
                int index = i;
                Vector2 anchor = i < 3 ? new Vector2(.2f + .3f * i, .57f) : new Vector2(.35f + .3f * (i - 3), .3f);
                ModeButton("PracticeStage" + (i + 1), $"Stage {i + 1}\n{stageNames[i]}", anchor, practicePanel.transform,
                    () => { practiceStage = index; BeginMode(RunMode.Practice); });
            }
            ModeButton("PracticeBack", "뒤로가기", new Vector2(.5f, .1f), practicePanel.transform, ShowModeSelection);
            var back = practiceButtons[practiceButtons.Count - 1];
            back.GetComponent<RectTransform>().sizeDelta = new Vector2(180, 55);
            var backLabel = back.GetComponentInChildren<UnityEngine.UI.Text>();
            backLabel.rectTransform.sizeDelta = new Vector2(160, 45); backLabel.resizeTextMaxSize = 26;
            practicePanel.SetActive(false);
            modePanel.SetActive(false);
            SelectMenu(modeButtons, 0); SelectMenu(practiceButtons, 0);
        }

        void BuildDeveloperToggle()
        {
            var item = new GameObject("DeveloperMode", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Toggle));
            item.transform.SetParent(modePanel.transform, false);
            var rect = item.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(20, -20); rect.sizeDelta = new Vector2(26, 26);
            var background = item.GetComponent<UnityEngine.UI.Image>(); background.color = Color.black;
            var outline = item.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = Accent; outline.effectDistance = new Vector2(1.5f, -1.5f);
            var check = new GameObject("Check", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            check.transform.SetParent(item.transform, false);
            var checkRect = check.GetComponent<RectTransform>();
            checkRect.anchorMin = checkRect.anchorMax = Vector2.one * .5f; checkRect.sizeDelta = new Vector2(16, 16);
            var mark = check.GetComponent<UnityEngine.UI.Image>(); mark.color = Accent; mark.raycastTarget = false;
            developerToggle = item.GetComponent<UnityEngine.UI.Toggle>();
            developerToggle.targetGraphic = background; developerToggle.graphic = mark;
            developerToggle.transition = UnityEngine.UI.Selectable.Transition.None;
            developerToggle.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            developerToggle.isOn = session.DeveloperMode;
            developerToggle.onValueChanged.AddListener(value =>
            {
                session.DeveloperMode = value;
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            });
            item.SetActive(false);
        }

        void ModeButton(string name, string caption, Vector2 anchor, Transform parent, Action selected)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            item.transform.SetParent(parent, false);
            var rect = item.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor; rect.sizeDelta = new Vector2(300, 130);
            var image = item.GetComponent<UnityEngine.UI.Image>(); image.color = Accent;
            var button = item.GetComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            var outline = item.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = Accent; outline.effectDistance = new Vector2(2, -2);
            var buttons = parent == modePanel.transform ? modeButtons : practiceButtons;
            int index = buttons.Count; buttons.Add(button);
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                if (session.Phase.Value != GamePhase.Title || starting) return;
                SelectMenu(buttons, index);
                selected();
            });
            var label = ResultLabel(name + "Label", Vector2.one * .5f, new Vector2(280, 110), 38);
            label.transform.SetParent(item.transform, false); label.text = caption; label.color = Color.black;
        }

        void SelectMenu(List<UnityEngine.UI.Button> buttons, int index)
        {
            if (buttons == modeButtons) modeSelection = index;
            else if (buttons == practiceButtons) practiceSelection = index;
            else titleSelection = index;
            for (int i = 0; i < buttons.Count; i++)
            {
                bool selected = i == index;
                buttons[i].GetComponent<UnityEngine.UI.Image>().color = selected ? Accent : Color.black;
                buttons[i].GetComponent<UnityEngine.UI.Outline>().enabled = !selected;
                buttons[i].GetComponentInChildren<UnityEngine.UI.Text>().color = selected ? Color.black : Accent;
            }
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }

        void SelectPracticeRow(int direction)
        {
            Vector2 current = practiceButtons[practiceSelection].GetComponent<RectTransform>().anchorMin;
            float row = current.y, distance = float.PositiveInfinity;
            foreach (var button in practiceButtons)
            {
                float y = button.GetComponent<RectTransform>().anchorMin.y;
                float delta = (y - current.y) * direction;
                if (delta > .001f && delta < distance) { distance = delta; row = y; }
            }
            if (float.IsPositiveInfinity(distance)) return;
            int selected = practiceSelection; float nearest = float.PositiveInfinity;
            for (int i = 0; i < practiceButtons.Count; i++)
            {
                Vector2 anchor = practiceButtons[i].GetComponent<RectTransform>().anchorMin;
                float gap = Mathf.Abs(anchor.x - current.x);
                if (Mathf.Abs(anchor.y - row) < .001f && gap < nearest) { nearest = gap; selected = i; }
            }
            SelectMenu(practiceButtons, selected);
        }

        void BeginMode(RunMode mode)
        {
            runMode = mode; modePanel.SetActive(false); practicePanel.SetActive(false); input.Clear();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            StartRun().Forget(Debug.LogException);
        }

        void ShowModeSelection()
        {
            if (session.Phase.Value != GamePhase.Title || starting) return;
            if (enableStageTesting) { StartRun().Forget(Debug.LogException); return; }
            practicePanel.SetActive(false); modePanel.SetActive(true); modePanel.transform.SetAsLastSibling();
            SelectMenu(modeButtons, 0);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }

        void ShowVictory()
        {
            session.Phase.Value = GamePhase.Victory;
            gameOverFrame = Time.frameCount;
            player.gameObject.SetActive(false); hudPanel.SetActive(false);
            resultRank.text = runMode == RunMode.Practice ? "연습\n완료" : "CLEAR!";
            resultRank.resizeTextMaxSize = 280;
            resultScore.rectTransform.sizeDelta = new Vector2(480, 220);
            resultScore.text = runMode == RunMode.Practice ? $"잃은 목숨: {session.LostLives}"
                : $"축하합니다!\n무한모드가 열렸습니다!\n점수: {session.Score.Value}";
            gameOverPanel.transform.SetAsLastSibling();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }

        async UniTask StartRun()
        {
            if (starting || session.Phase.Value != GamePhase.Title) return;
            starting = true;
            runCancellation?.Dispose();
            runCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            var token = runCancellation.Token;
            session.NewRun(runMode == RunMode.Practice);
            titleStatus.text = "밈 불러오는 중…";
            try
            {
                if (stages == null)
                {
                    var loadedStages = new StageDefinition[5];
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
            int chosen;
            if (enableStageTesting && testStageNumber >= 0)
            {
                chosen = Mathf.Clamp(testStageNumber, 0, stages.Length - 1);
                if (chosen != testStageNumber)
                    Debug.LogWarning($"Test stage {testStageNumber} does not exist. Using stage {chosen}.", this);
            }
            else if (runMode == RunMode.Practice)
                chosen = Mathf.Clamp(practiceStage, 0, stages.Length - 1);
            else if (runMode == RunMode.Normal)
                chosen = Mathf.Clamp(session.ClearedStages.Value, 0, stages.Length - 1);
            else
            {
                chosen = UnityEngine.Random.Range(0, lastStage < 0 ? stages.Length : stages.Length - 1);
                if (lastStage >= 0 && chosen >= lastStage) chosen++;
            }
            lastStage = chosen;
            var stage = stages[chosen];
            if (stageLayout != null) { stageLayout.SetActive(false); Destroy(stageLayout); }
            if (defaultPlatforms != null)
                foreach (var platform in defaultPlatforms) if (platform != null) platform.SetActive(stage.layoutPrefab == null);
            if (stage.layoutPrefab != null) stageLayout = Instantiate(stage.layoutPrefab, transform);
            if (stageLayout != null)
            {
                foreach (var danger in stageLayout.GetComponentsInChildren<Danger>(true)) danger.Configure(this);
            }
            stageText.text = $"STAGE {(runMode == RunMode.Practice ? chosen + 1 : session.ClearedStages.Value + 1)}  /  {stage.title}";
            messageText.text = stage.instruction;
            session.Phase.Value = GamePhase.Playing;
            float stageDuration = stage.duration;
            var gamst = stageLayout == null ? null : stageLayout.GetComponent<GamstStage>();
            var bringL = stageLayout == null ? null : stageLayout.GetComponent<BringLStage>();
            var sixSeven = stageLayout == null ? null : stageLayout.GetComponent<SixSevenStage>();
            var geoje = stageLayout == null ? null : stageLayout.GetComponent<GeojeStage>();
            var jwejwei = stageLayout == null ? null : stageLayout.GetComponent<JwejweiStage>();
            if (gamst != null)
            {
                foreach (var director in stageLayout.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true)) director.Stop();
                gamst.Begin(this);
                stageDuration = gamst.Duration;
            }
            else if (bringL != null)
            {
                foreach (var director in stageLayout.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true)) director.Stop();
                bringL.Begin(this);
                stageDuration = bringL.Duration;
            }
            else if (sixSeven != null)
            {
                foreach (var director in stageLayout.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true)) director.Stop();
                sixSeven.Begin(this);
                stageDuration = sixSeven.Duration;
            }
            else if (geoje != null)
            {
                geoje.Begin(this);
                stageDuration = geoje.Duration;
            }
            else if (jwejwei != null)
            {
                jwejwei.Begin(this);
                stageDuration = jwejwei.Duration;
            }
            else if (stageLayout != null)
                foreach (var director in stageLayout.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true))
                    if (director.playableAsset != null) stageDuration = Mathf.Max(stageDuration, (float)director.duration);
            float elapsed = 0;
            // Legacy automatic obstacles are disabled while stages are rebuilt.
            // float nextAttack = .8f;
            progress.value = 0;
            while (session.Phase.Value == GamePhase.Playing)
            {
                token.ThrowIfCancellationRequested();
                elapsed += Time.deltaTime;
                session.AdvanceScore(Time.deltaTime);
                progress.value = Mathf.Clamp01(elapsed / stageDuration);
                timerText.text = $"{Mathf.Max(0, Mathf.CeilToInt(stageDuration - elapsed))}s";
                if (elapsed >= stageDuration && (gamst == null || gamst.Completed) && (bringL == null || bringL.Completed)
                    && (sixSeven == null || sixSeven.Completed) && (geoje == null || geoje.Completed)
                    && (jwejwei == null || jwejwei.Completed)) break;
                // if (elapsed >= nextAttack)
                // {
                //     Attack(stage, stageToken).Forget(error => { if (error is not OperationCanceledException) Debug.LogException(error); });
                //     nextAttack += stage.attackInterval;
                // }
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            stageCancellation.Cancel();
            StopStageTimeline();
            if (session.ClearStage())
            {
                ClearDangers();
                if (runMode == RunMode.Practice || runMode == RunMode.Normal && !enableStageTesting && session.ClearedStages.Value >= stages.Length)
                {
                    ShowVictory();
                    return;
                }
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
            for (int i = 0; i < 2; i++)
            {
                var bar = new GameObject(i == 0 ? "CrossHorizontal" : "CrossVertical");
                bar.transform.SetParent(healthPickup.transform, false);
                bar.transform.localPosition = Vector3.zero;
                bar.transform.localScale = i == 0 ? new Vector3(.65f, .12f, 1) : new Vector3(.12f, .65f, 1);
                var render = bar.AddComponent<SpriteRenderer>();
                render.sprite = solidSprite; render.color = Accent; render.sortingOrder = 7;
            }
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

        public void HitPlayer(bool ignoreInvulnerability = false)
        {
            if (!session.Damage(Time.time, ignoreInvulnerability)) return;
            playerArt.DOKill();
            playerArt.localScale = Vector3.one;
            playerArt.DOPunchScale(new Vector3(.15f, .15f, 0), .25f, 6).SetLink(playerArt.gameObject);
            Camera.main.transform.DOShakePosition(.2f, .15f, 12).SetLink(Camera.main.gameObject);
            invulnerabilityBlink?.Kill();
            invulnerabilityBlink = DOTween.To(() => 1f, SetPlayerOpacity, .2f, .1f)
                .SetLoops(20, LoopType.Yoyo).SetEase(Ease.Linear).SetLink(playerArt.gameObject)
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

        void EndRun()
        {
            StopStageTimeline();
            runCancellation?.Cancel();
            ClearDangers();
            gameOverFrame = Time.frameCount;
            if (runMode == RunMode.Normal)
            {
                int diedStage = Mathf.Clamp(lastStage + 1, 1, 5);
                resultRank.resizeTextMaxSize = 48;
                resultRank.text = $"현재 스테이지: {diedStage}\n남은 스테이지: {5 - diedStage}";
            }
            else
            {
                resultRank.resizeTextMaxSize = 280;
                resultRank.text = GameSession.RankForScore(session.Score.Value);
            }
            resultScore.rectTransform.sizeDelta = new Vector2(430, 100);
            resultScore.text = $"점수: {session.Score.Value}";
            hudPanel.SetActive(false);
            gameOverPanel.transform.SetAsLastSibling();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            player.gameObject.SetActive(false);
        }
        void StopStageTimeline()
        {
            if (stageLayout == null) return;
            stageLayout.GetComponent<GamstStage>()?.Stop();
            stageLayout.GetComponent<BringLStage>()?.Stop();
            stageLayout.GetComponent<SixSevenStage>()?.Stop();
            stageLayout.GetComponent<GeojeStage>()?.Stop();
            stageLayout.GetComponent<JwejweiStage>()?.Stop();
            foreach (var director in stageLayout.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true)) director.Stop();
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
