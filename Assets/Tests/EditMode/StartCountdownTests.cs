using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 시작 카운트다운(StartCountdownController), 공용 메시지(CenterMessageUI), GameManager Ready 대기, Stretching 요청 테스트 (EditMode).
/// EditMode에서는 Awake/Start/OnEnable/Update가 자동 호출되지 않으므로 직접 호출하고 Tick(dt)으로 시간을 진행한다.
/// </summary>
public class StartCountdownTests
{
    private const float Dt = 0.05f;

    private BestTimeBackup bestTimeBackup;
    private GameObject root;
    private GameManager gm;
    private SurvivalTimer timer;
    private MissileSpawner spawner;
    private PlayerController player;
    private PlayerInputRouter router;
    private PlayerAnimationController anim;
    private CenterMessageUI messageUI;
    private GameObject panel;
    private TextMeshProUGUI text;
    private StartCountdownController countdown;

    [SetUp]
    public void SetUp()
    {
        bestTimeBackup = BestTimeBackup.Capture();
        BestTimeBackup.ClearForTest();

        root = new GameObject("CountdownTestRoot");

        var arenaGo = Child("Arena");
        var arena = arenaGo.AddComponent<ArenaBounds>();
        arena.Center = new Vector2(0.5f, -0.5f);
        arena.HalfSize = 2f;

        var playerGo = Child("Player");
        playerGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        var col = playerGo.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.3f;
        var visual = new GameObject("Visual");
        visual.transform.SetParent(playerGo.transform, false);
        router = playerGo.AddComponent<PlayerInputRouter>();
        player = playerGo.AddComponent<PlayerController>();
        Set(player, "inputRouter", router);
        Set(player, "arenaBounds", arena);
        Set(player, "groundCollider", col);
        player.ResetPlayer(arena.Center);
        anim = visual.AddComponent<PlayerAnimationController>();
        Set(anim, "playerController", player);

        var systems = Child("GameSystems");
        timer = systems.AddComponent<SurvivalTimer>();
        spawner = Child("MissileSpawner").AddComponent<MissileSpawner>();
        gm = systems.AddComponent<GameManager>();
        Set(gm, "playerController", player);
        Set(gm, "missileSpawner", spawner);
        Set(gm, "survivalTimer", timer);
        Set(gm, "inputRouter", router);

        var canvas = Child("UICanvas");
        panel = new GameObject("TextPanel", typeof(RectTransform));
        panel.transform.SetParent(canvas.transform, false);
        text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        text.transform.SetParent(panel.transform, false);
        messageUI = canvas.AddComponent<CenterMessageUI>();
        Set(messageUI, "panel", panel);
        Set(messageUI, "messageText", text);

        countdown = canvas.AddComponent<StartCountdownController>();
        Set(countdown, "gameManager", gm);
        Set(countdown, "messageUI", messageUI);
        Set(countdown, "playerAnimation", anim);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        bestTimeBackup.Restore();
    }

    private GameObject Child(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        return go;
    }

    private static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Call(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, null);
    }

    /// <summary>씬 로드 순서 재현: 모든 Awake → 모든 Start.</summary>
    private void SimulateSceneStart()
    {
        Call(messageUI, "Awake");
        Call(countdown, "Awake");
        Call(gm, "Awake");
        Call(gm, "Start");
        Call(countdown, "Start");
    }

    /// <summary>게임 시간을 진행: 카운트다운, 타이머, 스포너, 플레이어(키 없는 입력)를 함께 Tick.</summary>
    private void Advance(float seconds, List<string> shownMessages = null)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            timer.Tick(Dt);
            countdown.Tick(Dt);
            router.SubmitInput(Vector2.right, true); // 카운트다운 중 이동/점프 입력 시도
            player.Tick(Dt);
            if (shownMessages != null && messageUI.IsVisible &&
                (shownMessages.Count == 0 || shownMessages[shownMessages.Count - 1] != messageUI.CurrentMessage))
            {
                shownMessages.Add(messageUI.CurrentMessage);
            }
        }
    }

    // ---------- 순수 계산 ----------

    [TestCase(0f, "3")]
    [TestCase(0.99f, "3")]
    [TestCase(1.0f, "2")]
    [TestCase(1.5f, "2")]
    [TestCase(2.0f, "1")]
    [TestCase(2.99f, "1")]
    [TestCase(3.0f, "START!")]
    [TestCase(3.59f, "START!")]
    public void ResolveMessage_DefaultSequence(float elapsed, string expected)
    {
        Assert.AreEqual(expected, StartCountdownController.ResolveMessage(elapsed, 3, 1f, "START!"));
    }

    [Test]
    public void TotalDuration_Default_Is3Point6Seconds()
    {
        Assert.AreEqual(3.6f, StartCountdownController.ComputeTotalDuration(3, 1f, 0.6f), 1e-5f);
        Assert.AreEqual(3.6f, countdown.TotalDuration, 1e-5f, "Inspector 기본값");
    }

    // ---------- 시작 흐름 ----------

    [Test]
    public void SceneStart_DisablesAutoStart_AndWaitsInReady()
    {
        SimulateSceneStart();

        Assert.IsFalse(gm.StartGameOnStart, "카운트다운이 GameManager 자동 시작을 끔");
        Assert.AreEqual(GameState.Ready, gm.CurrentState);
        Assert.IsTrue(countdown.IsRunning);
        Assert.AreEqual("3", messageUI.CurrentMessage);
        Assert.IsTrue(panel.activeSelf);
        Assert.AreEqual("3", text.text);
    }

    [Test]
    public void DuringCountdown_ReadyKept_NoTime_NoSpawn_NoMovement()
    {
        SimulateSceneStart();
        Vector2 startPos = player.GroundPosition;

        Advance(3.5f);

        Assert.AreEqual(GameState.Ready, gm.CurrentState);
        Assert.AreEqual(0f, timer.ElapsedTime, "생존시간 증가 없음");
        Assert.IsFalse(timer.IsRunning);
        Assert.IsFalse(spawner.IsSpawning, "미사일 생성 없음");
        Assert.AreEqual(0, spawner.ActiveMissileCount);
        Assert.IsFalse(player.CanControl);
        Assert.IsFalse(router.InputEnabled);
        Assert.AreEqual(startPos, player.GroundPosition, "이동 없음");
        Assert.IsTrue(player.IsGrounded, "점프 없음");
        Assert.AreEqual(0, countdown.StartGameRequestCount);
    }

    [Test]
    public void Countdown_ShowsMessagesInOrder_ThenStartsGameOnce()
    {
        SimulateSceneStart();
        var shown = new List<string> { messageUI.CurrentMessage };

        Advance(3.7f, shown);

        CollectionAssert.AreEqual(new[] { "3", "2", "1", "START!" }, shown);
        Assert.IsFalse(messageUI.IsVisible, "START! 이후 메시지 숨김");
        Assert.IsFalse(panel.activeSelf);
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.AreEqual(1, countdown.StartGameRequestCount);
        Assert.IsTrue(countdown.IsCompleted);
        Assert.IsTrue(timer.IsRunning);
        Assert.IsTrue(spawner.IsSpawning);
        Assert.Less(spawner.SpawnTimer, 0.2f, "첫 생성 대기는 Playing 시작 기준");
        Assert.IsTrue(player.CanControl);
        Assert.IsTrue(router.InputEnabled);
    }

    [Test]
    public void StartGame_NotCalledTwice()
    {
        SimulateSceneStart();
        countdown.BeginCountdown(); // 진행 중 중복 시작 → 무시
        Advance(1.5f);
        Assert.AreEqual("2", countdown.CurrentMessage, "중복 시작이 카운트다운을 초기화하지 않음");

        Advance(3f);
        countdown.BeginCountdown(); // 이미 Playing → 무시
        Advance(5f);

        Assert.AreEqual(1, countdown.StartGameRequestCount);
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.IsFalse(countdown.IsRunning);
    }

    [Test]
    public void Disable_CancelsCountdown_ReenableRestartsFromThree()
    {
        SimulateSceneStart();
        Advance(1.5f);

        Call(countdown, "OnDisable");
        Assert.IsFalse(countdown.IsRunning);
        Assert.IsFalse(messageUI.IsVisible, "중단 시 메시지 숨김");
        Advance(5f);
        Assert.AreEqual(GameState.Ready, gm.CurrentState, "중단되면 게임을 시작하지 않음");
        Assert.AreEqual(0, countdown.StartGameRequestCount);

        Call(countdown, "OnEnable");
        Assert.IsTrue(countdown.IsRunning);
        Assert.AreEqual("3", messageUI.CurrentMessage, "처음부터 다시");
        Advance(3.7f);
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.AreEqual(1, countdown.StartGameRequestCount);
    }

    [Test]
    public void InspectorDurations_AreApplied()
    {
        var so = new SerializedObject(countdown);
        so.FindProperty("countFrom").intValue = 2;
        so.FindProperty("secondsPerCount").floatValue = 0.5f;
        so.FindProperty("startMessage").stringValue = "GO";
        so.FindProperty("startMessageDuration").floatValue = 0.2f;
        so.ApplyModifiedPropertiesWithoutUndo();

        SimulateSceneStart();
        var shown = new List<string> { messageUI.CurrentMessage };
        Advance(1.3f, shown); // 총 2 × 0.5 + 0.2 = 1.2초

        CollectionAssert.AreEqual(new[] { "2", "1", "GO" }, shown);
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
    }

    [Test]
    public void GameManagerAutoStart_StillWorksWithoutCountdown()
    {
        Object.DestroyImmediate(countdown);
        Call(gm, "Awake");
        Call(gm, "Start");
        Assert.AreEqual(GameState.Playing, gm.CurrentState, "카운트다운이 없으면 기존처럼 즉시 시작");
    }

    // ---------- Stretching ----------

    [Test]
    public void CountdownStart_RequestsStretching()
    {
        SimulateSceneStart();
        Assert.IsTrue(anim.IsPlayingStretching);
    }

    [Test]
    public void Stretching_WithoutPlayableAnimator_FallsBackToIdle()
    {
        anim.PlayStretching();
        anim.Tick(Dt); // EditMode: Animator 재생 불가 → 기다리지 않고 Idle

        Assert.IsFalse(anim.IsPlayingStretching);
        Assert.AreEqual(PlayerAnimationController.AnimState.Idle, anim.CurrentState);
        StringAssert.StartsWith("Idle", anim.CurrentStateName);
    }

    [Test]
    public void Stretching_IgnoredWhenDead()
    {
        player.Die();
        anim.PlayStretching();
        Assert.IsFalse(anim.IsPlayingStretching);
    }

    [Test]
    public void GetStateName_Stretching()
    {
        Assert.AreEqual("Stretching", PlayerAnimationController.GetStateName(PlayerAnimationController.AnimState.Stretching, Missile.Direction8.East));
        CollectionAssert.DoesNotContain(PlayerAnimationController.GetAllStateNames(), "Stretching", "기존 33개 State 목록은 변경 없음");
    }

    [Test]
    public void VisualController_StretchingState_OnceWithoutTransitions_SpriteRendererClip()
    {
        const string path = "Assets/Art/Cahracters/Animations/Anims/Visual.controller";
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
        Assert.IsNotNull(controller, path);
        UnityEditor.Animations.AnimatorState stretching = null;
        foreach (var child in controller.layers[0].stateMachine.states)
        {
            if (child.state.name == PlayerAnimationController.StretchingStateName) stretching = child.state;
        }
        Assert.IsNotNull(stretching, "Visual.controller에 Stretching State 필요");
        Assert.IsEmpty(stretching.transitions, "전환은 코드(Play)로만: Stretching → Idle Transition 없음");
        Assert.AreNotSame(stretching, controller.layers[0].stateMachine.defaultState, "기본 State는 기존 Idle 유지");

        var clip = stretching.motion as AnimationClip;
        Assert.IsNotNull(clip);
        Assert.IsFalse(AnimationUtility.GetAnimationClipSettings(clip).loopTime, "Stretching Loop Time 꺼짐");
        Assert.AreEqual(0, AnimationUtility.GetCurveBindings(clip).Length, "Transform 커브 없음(점프 높이와 충돌 방지)");
        foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            Assert.AreEqual(typeof(SpriteRenderer), b.type);
            Assert.AreEqual("m_Sprite", b.propertyName);
        }
    }

    // ---------- 메시지 공유 (카운트다운 / GameOverUI) ----------

    private GameOverUI CreateGameOverUI()
    {
        var go = Child("GameOverUI");
        var ui = go.AddComponent<GameOverUI>();
        Set(ui, "gameManager", gm);
        Set(ui, "messageUI", messageUI);
        return ui;
    }

    [Test]
    public void GameOverUI_DoesNotHideCountdown_AndCountdownDoesNotHideGameOver()
    {
        var gameOverUI = CreateGameOverUI();
        SimulateSceneStart();
        Call(gameOverUI, "Awake");   // 늦게 실행된 Awake Hide → 카운트다운 메시지 유지
        Call(gameOverUI, "OnEnable");
        Assert.AreEqual("3", messageUI.CurrentMessage);
        Assert.IsTrue(panel.activeSelf);

        Advance(3.7f); // Playing 전환 → GameOverUI.Hide 호출되어도 문제 없음
        Assert.IsFalse(messageUI.IsVisible);

        timer.Tick(2f);
        gm.EndGame();
        anim.Tick(Dt); // Death 처리(EditMode: Animator 재생 불가 → 즉시 완료) → OnGameResultReady → GameOverUI
        Assert.AreEqual("GAME OVER\nNEW RECORD!", messageUI.CurrentMessage);
        Assert.AreEqual("GAME OVER\nNEW RECORD!", text.text);
        Assert.AreSame(gameOverUI, messageUI.CurrentOwner);

        Call(countdown, "OnDisable"); // 카운트다운 쪽의 늦은 정리가 게임 오버 메시지를 지우지 않음
        Assert.IsTrue(panel.activeSelf);
        Assert.AreEqual("GAME OVER\nNEW RECORD!", messageUI.CurrentMessage);
        Call(gameOverUI, "OnDisable");
    }

    [Test]
    public void GameOverUI_NotNewRecord_ShowsGameOverOnly()
    {
        RecordStore.TryUpdateRecord(100f);
        var gameOverUI = CreateGameOverUI();
        Call(gameOverUI, "Awake");
        Call(gameOverUI, "OnEnable");
        gm.StartGame();
        timer.Tick(1f);
        gm.EndGame();
        anim.Tick(Dt); // Death 완료 → 결과 전달

        Assert.AreEqual("GAME OVER", messageUI.CurrentMessage);
        Call(gameOverUI, "OnDisable");
    }

    [Test]
    public void GameOverUI_WithNewRecordObject_ShowsIcon_OnlyOnNewRecord_HiddenOnRestart()
    {
        // 실제 Play 씬 구성: TextPanel/BestIcon을 New Record Object로 연결
        var icon = new GameObject("BestIcon", typeof(RectTransform));
        icon.transform.SetParent(panel.transform, false);
        var gameOverUI = CreateGameOverUI();
        Set(gameOverUI, "newRecordObject", icon);
        Call(gameOverUI, "Awake");
        Call(gameOverUI, "OnEnable");
        Assert.IsFalse(icon.activeSelf, "시작 시 숨김");

        gm.StartGame();
        timer.Tick(1f);
        gm.EndGame();
        anim.Tick(Dt);
        Assert.IsTrue(gm.LastGameResult.IsNewRecord);
        Assert.AreEqual("GAME OVER", messageUI.CurrentMessage, "전용 오브젝트가 있으면 문구를 붙이지 않음");
        Assert.IsTrue(icon.activeSelf, "신기록 아이콘 표시");

        gm.StartGame(); // 재시작 → 숨김
        anim.Tick(Dt);  // 실제 게임의 다음 프레임 Update: 사망 해제 → 이전 Death 추적 초기화
        Assert.IsFalse(icon.activeSelf);
        Assert.IsFalse(messageUI.IsVisible);

        timer.Tick(0.5f); // 기존 기록(1초) 미만
        gm.EndGame();
        anim.Tick(Dt);
        Assert.IsFalse(gm.LastGameResult.IsNewRecord);
        Assert.AreEqual("GAME OVER", messageUI.CurrentMessage);
        Assert.IsFalse(icon.activeSelf, "신기록이 아니면 아이콘 없음");
        Call(gameOverUI, "OnDisable");
    }

    [Test]
    public void CenterMessageUI_HideOnlyByOwner()
    {
        var a = Child("A").transform;
        var b = Child("B").transform;
        messageUI.Show(a, "hello");
        Assert.IsFalse(messageUI.Hide(b));
        Assert.IsTrue(panel.activeSelf);
        Assert.IsTrue(messageUI.Hide(a));
        Assert.IsFalse(panel.activeSelf);
        Assert.IsTrue(messageUI.Hide(b), "표시 중인 메시지가 없으면 누구나 숨김 가능");
    }
}
