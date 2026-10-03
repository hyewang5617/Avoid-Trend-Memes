using System;
using System.IO;
using MemeDodge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public static class CreateGameScene
{
    const string Root = "Assets/Game";
    const string ScenePath = "Assets/Scenes/AvoidTrendMemes.unity";
    static Font font;
    static Sprite sprite;
    static Material lineMaterial;

    [MenuItem("Tools/Avoid Trend Memes/Create Game Scene")]
    public static void Create()
    {
        if (File.Exists(ScenePath))
        {
            Debug.Log("Game scene already exists; existing work was preserved.");
            return;
        }
        if (!EditorSceneManager.SaveOpenScenes()) throw new InvalidOperationException("Could not save the existing scene.");
        foreach (var directory in new[] { Root, Root + "/Stages", Root + "/Audio", Root + "/Art", Root + "/Fonts" }) Directory.CreateDirectory(directory);
        string fontPath = Root + "/Fonts/Malgun.ttf";
        if (!File.Exists(fontPath)) File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf"), fontPath);
        var texture = new Texture2D(8, 8);
        var pixels = new Color[64];
        Array.Fill(pixels, Color.white);
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(Root + "/Art/Solid.png", texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.Refresh();
        var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/Art/Solid.png");
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = 8;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();
        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Art/Solid.png");
        font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
        lineMaterial = new Material(Shader.Find("Sprites/Default"));
        AssetDatabase.CreateAsset(lineMaterial, Root + "/Art/Line.mat");
        PrepareContent();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(AspectCamera));
        var camera = cameraObject.GetComponent<Camera>();
        cameraObject.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true;
        camera.orthographicSize = 5;
        camera.backgroundColor = Color.black;
        camera.clearFlags = CameraClearFlags.SolidColor;
        var root = new GameObject("Game", typeof(AudioSource));
        var controller = root.AddComponent<GameController>();
        root.AddComponent<GameScope>();
        controller.sound = root.GetComponent<AudioSource>();
        controller.sound.playOnAwake = false;
        controller.solidSprite = sprite;
        controller.worldFont = font;
        controller.lineMaterial = lineMaterial;
        controller.input = root.AddComponent<MemeDodge.PlayerInput>();
        controller.dangerRoot = new GameObject("Dangers").transform;
        controller.dangerRoot.SetParent(root.transform);
        Box("Floor", new Vector2(0, -3.5f), new Vector2(17, .4f), true);
        Box("LeftWall", new Vector2(-8.6f, 0), new Vector2(.3f, 12), true);
        Box("RightWall", new Vector2(8.6f, 0), new Vector2(.3f, 12), true);
        var leftPlatform = Box("LeftPlatform", new Vector2(-5, -1.5f), new Vector2(2, .2f), true);
        var rightPlatform = Box("RightPlatform", new Vector2(5, -.8f), new Vector2(2, .2f), true);
        leftPlatform.AddComponent<OneWayPlatform>();
        rightPlatform.AddComponent<OneWayPlatform>();
        controller.defaultPlatforms = new[] { leftPlatform, rightPlatform };
        var playerObject = new GameObject("Player", typeof(Rigidbody2D), typeof(BoxCollider2D));
        playerObject.transform.position = new Vector3(0, -2.7f);
        var body = playerObject.GetComponent<Rigidbody2D>();
        body.gravityScale = 3;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        playerObject.GetComponent<BoxCollider2D>().size = new Vector2(.4f, .4f);
        controller.player = playerObject.AddComponent<PlayerMotor>();
        controller.player.Configure(controller.input, 1 << 8);
        var art = new GameObject("Art");
        art.transform.SetParent(playerObject.transform, false);
        controller.playerArt = art.transform;
        var square = new GameObject("Square"); square.transform.SetParent(art.transform, false);
        var playerSprite = square.AddComponent<SpriteRenderer>();
        playerSprite.sprite = sprite; playerSprite.color = GameController.Accent; playerSprite.sortingOrder = 10;
        square.transform.localScale = new Vector3(.4f, .4f, 1);

        var canvasObject = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;
        var content = Panel(canvas.transform, "Content", Color.clear);
        var fitter = content.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 16f / 9f;
        controller.hudPanel = Panel(content.transform, "HUD", Color.clear);
        controller.healthText = Label(controller.hudPanel.transform, "HP", "HP", new Vector2(.78f, .91f), new Vector2(350, 55), 28);
        controller.stageText = Label(controller.hudPanel.transform, "Stage", "", new Vector2(.3f, .92f), new Vector2(670, 60), 27);
        controller.timerText = Label(controller.hudPanel.transform, "Timer", "", new Vector2(.92f, .83f), new Vector2(120, 45), 30);
        controller.messageText = Label(controller.hudPanel.transform, "Message", "", new Vector2(.5f, .78f), new Vector2(950, 50), 22);
        Label(controller.hudPanel.transform, "Controls", "←  →  이동       SPACE  더블 점프", new Vector2(.5f, .075f), new Vector2(900, 50), 26);
        var progressObject = new GameObject("Progress", typeof(RectTransform), typeof(Image), typeof(Slider));
        progressObject.transform.SetParent(controller.hudPanel.transform, false);
        Rect(progressObject, new Vector2(.5f, .85f), new Vector2(800, 6));
        progressObject.GetComponent<Image>().color = new Color(GameController.Accent.r, 1, GameController.Accent.b, .2f);
        controller.progress = progressObject.GetComponent<Slider>();
        controller.progress.interactable = false;
        var fill = Panel(progressObject.transform, "Fill", GameController.Accent);
        controller.progress.fillRect = fill.GetComponent<RectTransform>();
        controller.rewardPanel = Panel(content.transform, "Reward", new Color(0, 0, 0, .85f));
        Label(controller.rewardPanel.transform, "Clear", "밈 회피 성공!", new Vector2(.5f, .61f), new Vector2(600, 100), 48);
        controller.healthPackButton = Button(controller.rewardPanel.transform, "HealthPack", "힐팩 받기  +1 HP", new Vector2(.5f, .43f));
        controller.titlePanel = Panel(content.transform, "Title", Color.black);
        Label(controller.titlePanel.transform, "TitleName", "최신 밈 피하기", new Vector2(.5f, .7f), new Vector2(1050, 140), 70);
        Label(controller.titlePanel.transform, "EnglishName", "AVOID TREND MEMES", new Vector2(.5f, .55f), new Vector2(700, 50), 26);
        controller.startButton = Button(controller.titlePanel.transform, "Start", "시작하기", new Vector2(.5f, .36f));
        controller.titleStatus = Label(controller.titlePanel.transform, "Status", "←  →  이동    /    SPACE  더블 점프", new Vector2(.5f, .18f), new Vector2(1050, 60), 22);
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        PlayerSettings.productName = "Avoid Trend Memes";
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("Avoid Trend Memes scene created: " + ScenePath);
    }

    static void PrepareContent()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) settings = AddressableAssetSettings.Create("Assets/AddressableAssetsData", "AddressableAssetSettings", true, true);
        AddressableAssetSettingsDefaultObject.Settings = settings;
        var group = settings.FindGroup("Game Content") ?? settings.CreateGroup("Game Content", false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        string[] names = { "67 밈", "L을 가져와", "한남 말투 밈" };
        string[] instructions = { "6과 7이 떨어집니다. 예고선을 보고 피하세요!", "L을 뛰어넘고, 레이저가 켜지기 전에 점프!", "말풍선 탄막! 낮은 말은 점프, 높은 말은 아래로!" };
        for (int i = 0; i < 3; i++)
        {
            string path = $"{Root}/Stages/Stage{i}.asset";
            var stage = AssetDatabase.LoadAssetAtPath<StageDefinition>(path);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<StageDefinition>();
                stage.title = names[i]; stage.instruction = instructions[i]; stage.pattern = (MemePattern)i;
                stage.duration = 18; stage.attackInterval = i == 0 ? 1.35f : 2.1f; stage.warningDuration = .8f; stage.projectileSpeed = 4;
                AssetDatabase.CreateAsset(stage, path);
            }
            settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group).address = $"stage-{i}";
        }
        EditorUtility.SetDirty(settings);
    }

    static GameObject Box(string name, Vector2 position, Vector2 size, bool solid)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.position = position; go.transform.localScale = new Vector3(size.x, size.y, 1);
        go.GetComponent<SpriteRenderer>().sprite = sprite; go.GetComponent<SpriteRenderer>().color = GameController.Accent;
        if (solid) { go.layer = 8; go.AddComponent<BoxCollider2D>(); }
        return go;
    }
    static GameObject Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color; go.GetComponent<Image>().raycastTarget = color.a > 0;
        return go;
    }
    static Text Label(Transform parent, string name, string value, Vector2 anchor, Vector2 size, int fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false); Rect(go, anchor, size);
        var text = go.GetComponent<Text>(); text.font = font; text.text = value; text.fontSize = fontSize; text.color = GameController.Accent;
        text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text;
    }
    static Button Button(Transform parent, string name, string label, Vector2 anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false); Rect(go, anchor, new Vector2(430, 85));
        go.GetComponent<Image>().color = Color.black;
        var outline = go.AddComponent<Outline>(); outline.effectColor = GameController.Accent; outline.effectDistance = new Vector2(3, -3);
        Label(go.transform, "Label", label, new Vector2(.5f, .5f), new Vector2(420, 80), 32);
        var button = go.GetComponent<Button>(); var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = Color.white; colors.pressedColor = Color.gray; button.colors = colors;
        return button;
    }
    static void Rect(GameObject go, Vector2 anchor, Vector2 size)
    {
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size;
    }
}
