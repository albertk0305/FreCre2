using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stage 9 UI 테스트 (EditMode). Awake/OnEnable/Start가 자동 호출되지 않으므로 필요한 경우 직접 호출한다.
/// 실제 PlayerPrefs(최고기록/조작 방식)는 백업 후 복원한다.
/// </summary>
public class UITests
{
    private BestTimeBackup bestTimeBackup;
    private bool hadControlMode;
    private int savedControlMode;
    private GameObject root;

    [SetUp]
    public void SetUp()
    {
        bestTimeBackup = BestTimeBackup.Capture();
        BestTimeBackup.ClearForTest();
        hadControlMode = PlayerPrefs.HasKey(ControlSettings.PlayerPrefsKey);
        savedControlMode = PlayerPrefs.GetInt(ControlSettings.PlayerPrefsKey, 0);
        PlayerPrefs.DeleteKey(ControlSettings.PlayerPrefsKey);
        root = new GameObject("UITestRoot");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        bestTimeBackup.Restore();
        if (hadControlMode) PlayerPrefs.SetInt(ControlSettings.PlayerPrefsKey, savedControlMode);
        else PlayerPrefs.DeleteKey(ControlSettings.PlayerPrefsKey);
        PlayerPrefs.Save();
    }

    private static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Call(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(target, null);
    }

    private T Child<T>(string name) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(root.transform, false);
        return go.AddComponent<T>();
    }

    // ---------- TimeFormatter ----------

    [TestCase(0f, "00:00.00")]
    [TestCase(5.03f, "00:05.03")]
    [TestCase(65.42f, "01:05.42")]
    [TestCase(59.999f, "00:59.99")]   // 내림: 60.00으로 올라가지 않음
    [TestCase(600f, "10:00.00")]
    [TestCase(-3f, "00:00.00")]
    [TestCase(float.NaN, "00:00.00")]
    [TestCase(float.PositiveInfinity, "00:00.00")]
    public void TimeFormatter_Format(float seconds, string expected)
    {
        Assert.AreEqual(expected, TimeFormatter.Format(seconds));
    }

    // float로 저장하면 정확한 값보다 약간 작아지는 값들 (예: 0.29f = 0.28999999…, 65.42f = 65.4199981…)
    [TestCase(0.29f, "00:00.29")]
    [TestCase(0.57f, "00:00.57")]
    [TestCase(1.15f, "00:01.15")]
    [TestCase(4.35f, "00:04.35")]
    [TestCase(65.42f, "01:05.42")]
    [TestCase(599.99f, "09:59.99")]
    public void TimeFormatter_ExactCentisecondFloat_NotShownAsPrevious(float seconds, string expected)
    {
        Assert.AreEqual(expected, TimeFormatter.Format(seconds));
    }

    // 정상적인 내림 규칙 유지: float로 구분 가능한 경계 직전 값은 이전 1/100초로 표시
    [TestCase(0.009f, "00:00.00")]
    [TestCase(0.019999f, "00:00.01")]
    [TestCase(1.009999f, "00:01.00")]
    [TestCase(59.999f, "00:59.99")]
    [TestCase(59.9999f, "00:59.99")]
    [TestCase(65.425f, "01:05.42")]
    [TestCase(65.4299f, "01:05.42")]
    [TestCase(3599.999f, "59:59.99")]
    public void TimeFormatter_JustBelowBoundary_FloorsToPrevious(float seconds, string expected)
    {
        Assert.AreEqual(expected, TimeFormatter.Format(seconds));
    }

    [Test]
    public void TimeFormatter_EveryCentisecondUpTo100Minutes_RoundTrips()
    {
        // n/100을 float로 만든 값은 항상 n 그대로 표시되어야 한다(float 표현 오차로 n-1이 되면 안 됨).
        for (int n = 0; n <= 600000; n++)
        {
            float seconds = n / 100f;
            int actual = TimeFormatter.ToCentiseconds(seconds);
            if (actual != n)
            {
                Assert.Fail($"{seconds:R}초 → {actual} (기대 {n})");
            }
        }
    }

    // ---------- MainMenuUI ----------

    private MainMenuUI CreateMainMenu(out TextMeshProUGUI best, out Image pc, out Image mobile)
    {
        best = Child<TextMeshProUGUI>("BestRecordText");
        pc = Child<Image>("PCButton");
        mobile = Child<Image>("MobileButton");
        var ui = root.AddComponent<MainMenuUI>();
        SetRef(ui, "bestRecordText", best);
        SetRef(ui, "pcButtonImage", pc);
        SetRef(ui, "mobileButtonImage", mobile);
        return ui;
    }

    [Test]
    public void MainMenu_ShowsBestRecord()
    {
        RecordStore.TryUpdateRecord(65.42f);
        var ui = CreateMainMenu(out var best, out _, out _);

        ui.RefreshBestTime();

        Assert.AreEqual("Best 01:05.42", best.text);
    }

    [Test]
    public void MainMenu_NoSavedMode_DefaultsToPC()
    {
        var ui = CreateMainMenu(out _, out var pc, out var mobile);

        ui.RefreshControlMode();

        Assert.AreEqual(ControlMode.PC, ui.SelectedMode);
        Assert.AreNotEqual(pc.color, mobile.color, "선택/비선택 버튼 색이 달라야 한다");
        Assert.AreEqual(Color.white, pc.color);
    }

    [Test]
    public void MainMenu_SelectMobile_SavesAndHighlights()
    {
        var ui = CreateMainMenu(out _, out var pc, out var mobile);

        ui.SelectMobile();

        Assert.AreEqual(ControlMode.Mobile, ControlSettings.GetControlMode());
        Assert.AreEqual(ControlMode.Mobile, ui.SelectedMode);
        Assert.AreEqual(Color.white, mobile.color);
        Assert.AreNotEqual(Color.white, pc.color);

        ui.SelectPC();

        Assert.AreEqual(ControlMode.PC, ControlSettings.GetControlMode());
        Assert.AreEqual(Color.white, pc.color);
        Assert.AreNotEqual(Color.white, mobile.color);
    }

    [Test]
    public void MainMenu_RestoresSavedMode()
    {
        ControlSettings.SetControlMode(ControlMode.Mobile);
        var ui = CreateMainMenu(out _, out _, out var mobile);

        ui.RefreshControlMode();

        Assert.AreEqual(ControlMode.Mobile, ui.SelectedMode);
        Assert.AreEqual(Color.white, mobile.color);
    }

    // ---------- PlayHUD ----------

    private PlayHUD CreateHud(SurvivalTimer timer, out TextMeshProUGUI survival, out TextMeshProUGUI best)
    {
        survival = Child<TextMeshProUGUI>("SurvivalText");
        best = Child<TextMeshProUGUI>("BestText");
        var hud = root.AddComponent<PlayHUD>();
        SetRef(hud, "survivalTimer", timer);
        SetRef(hud, "survivalTimeText", survival);
        SetRef(hud, "bestTimeText", best);
        return hud;
    }

    [Test]
    public void PlayHUD_ShowsElapsedAndBest_KeepsAfterStop_ResetsOnRestart()
    {
        RecordStore.TryUpdateRecord(10f);
        var timer = root.AddComponent<SurvivalTimer>();
        var hud = CreateHud(timer, out var survival, out var best);

        hud.RefreshBestTime();
        hud.RefreshSurvivalTime(force: true);
        Assert.AreEqual("00:10.00", best.text);
        Assert.AreEqual("00:00.00", survival.text);

        timer.StartTimer();
        timer.Tick(3.25f);
        hud.RefreshSurvivalTime();
        Assert.AreEqual("00:03.25", survival.text);

        timer.StopTimer();
        timer.Tick(5f); // 정지 후 증가하지 않음
        hud.RefreshSurvivalTime();
        Assert.AreEqual("00:03.25", survival.text, "게임 오버 후 마지막 생존시간 유지");

        timer.ResetTimer();
        hud.RefreshSurvivalTime();
        Assert.AreEqual("00:00.00", survival.text, "재시작 시 초기화");
        Assert.AreEqual("00:10.00", best.text, "HUD는 최고기록을 바꾸지 않는다");
        Assert.AreEqual(10f, RecordStore.GetBestTime());
    }

    // ---------- GameOverUI ----------

    private GameOverUI CreateGameOverUI(GameManager gm, out GameObject panel, out TextMeshProUGUI message)
    {
        panel = new GameObject("TextPanel", typeof(RectTransform));
        panel.transform.SetParent(root.transform, false);
        message = Child<TextMeshProUGUI>("Text");
        message.transform.SetParent(panel.transform, false);

        var uiGo = new GameObject("UICanvas");
        uiGo.transform.SetParent(root.transform, false);
        var ui = uiGo.AddComponent<GameOverUI>();
        SetRef(ui, "gameManager", gm);
        SetRef(ui, "gameOverPanel", panel);
        SetRef(ui, "messageText", message);
        Call(ui, "Awake");
        Call(ui, "OnEnable");
        return ui;
    }

    private GameManager CreateGameManager(out SurvivalTimer timer)
    {
        timer = root.AddComponent<SurvivalTimer>();
        var gm = root.AddComponent<GameManager>();
        SetRef(gm, "survivalTimer", timer);
        return gm;
    }

    [Test]
    public void GameOverUI_HiddenDuringPlay_ShownOnResult()
    {
        var gm = CreateGameManager(out var timer);
        var ui = CreateGameOverUI(gm, out var panel, out var message);

        Assert.IsFalse(panel.activeSelf, "시작 시 숨김");

        gm.StartGame();
        Assert.IsFalse(panel.activeSelf, "플레이 중 숨김");

        RecordStore.TryUpdateRecord(100f); // 이번 판은 신기록이 아님
        timer.Tick(2f);
        gm.EndGame(); // 사망 애니메이션 컨트롤러가 없으므로 결과가 즉시 전달된다

        Assert.IsTrue(ui.IsShown);
        Assert.IsTrue(panel.activeSelf);
        Assert.AreEqual("GAME OVER", message.text);
        Assert.AreEqual(100f, RecordStore.GetBestTime(), "GameOverUI는 기록을 바꾸지 않는다");

        Call(ui, "OnDisable");
    }

    [Test]
    public void GameOverUI_NewRecord_AddsMessage_AndRestartHides()
    {
        var gm = CreateGameManager(out var timer);
        var ui = CreateGameOverUI(gm, out var panel, out var message);

        gm.StartGame();
        timer.Tick(7.5f);
        gm.EndGame();

        Assert.IsTrue(gm.LastGameResult.IsNewRecord);
        Assert.AreEqual("GAME OVER\nNEW RECORD!", message.text);

        gm.StartGame(); // 재시작(같은 씬) → 초기 상태
        Assert.IsFalse(panel.activeSelf);
        Assert.IsFalse(ui.IsShown);
        Assert.AreEqual("GAME OVER", message.text);

        Call(ui, "OnDisable");
    }

    [Test]
    public void GameOverUI_Unsubscribed_IgnoresResult()
    {
        var gm = CreateGameManager(out _);
        var ui = CreateGameOverUI(gm, out var panel, out _);
        Call(ui, "OnDisable");

        gm.StartGame();
        gm.EndGame();

        Assert.IsFalse(panel.activeSelf);
    }

    // ---------- PlayerInputRouter: 모바일 조작 UI ----------

    [Test]
    public void InputRouter_Initialize_TogglesMobileControlsRoot()
    {
        var mobileRoot = new GameObject("MobileControls");
        mobileRoot.transform.SetParent(root.transform, false);
        var router = root.AddComponent<PlayerInputRouter>();
        SetRef(router, "mobileControlsRoot", mobileRoot);

        router.Initialize(ControlMode.PC);
        Assert.IsFalse(mobileRoot.activeSelf);

        router.Initialize(ControlMode.Mobile);
        Assert.IsTrue(mobileRoot.activeSelf);
        Assert.AreEqual(ControlMode.Mobile, router.CurrentControlMode);
    }

    [Test]
    public void InputRouter_Awake_AppliesSavedMode()
    {
        ControlSettings.SetControlMode(ControlMode.Mobile);
        var mobileRoot = new GameObject("MobileControls");
        mobileRoot.transform.SetParent(root.transform, false);
        mobileRoot.SetActive(false);
        var router = root.AddComponent<PlayerInputRouter>();
        SetRef(router, "mobileControlsRoot", mobileRoot);

        Call(router, "Awake");

        Assert.IsTrue(mobileRoot.activeSelf);
    }
}
