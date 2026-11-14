using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 시작 카운트다운 + Stretching 시작 연출 PlayMode 테스트.
/// - 리그 테스트: 실제 Visual.controller로 Stretching 1회 재생 → Idle 전환, 이동 시 즉시 양보.
/// - 씬 테스트: 실제 Play 씬(StartCountdownController 설정 후)에서 Ready 유지, 3/2/1/START!, Playing 시작, Restart/Exit.
/// </summary>
public class StartCountdownPlayModeTests
{
    private const string ControllerPath = "Assets/Art/Cahracters/Animations/Anims/Visual.controller";

    private BestTimeBackup bestTimeBackup;
    private GameObject root;

    [SetUp]
    public void SetUp()
    {
        bestTimeBackup = BestTimeBackup.Capture();
        BestTimeBackup.ClearForTest();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (root != null) Object.Destroy(root);
        Time.timeScale = 1f;
        bestTimeBackup.Restore();
        yield return null;
    }

    private static int Hash(string stateName) => Animator.StringToHash(PlayerAnimationController.LayerName + "." + stateName);

    private static IEnumerator WaitSeconds(float seconds)
    {
        float t0 = Time.time;
        while (Time.time - t0 < seconds) yield return null;
    }

    private struct Rig
    {
        public PlayerController Player;
        public PlayerInputRouter Router;
        public Animator Animator;
        public PlayerAnimationController Anim;
    }

    private Rig BuildRig()
    {
        var rig = new Rig();
#if UNITY_EDITOR
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        Assert.IsNotNull(controller, ControllerPath);
        root = new GameObject("StretchTestRoot");
        root.SetActive(false);
        var playerGo = new GameObject("Player");
        playerGo.transform.SetParent(root.transform, false);
        playerGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        playerGo.AddComponent<CircleCollider2D>().radius = 0.15f;
        rig.Router = playerGo.AddComponent<PlayerInputRouter>();
        rig.Router.enabled = false; // 실제 키보드가 SubmitInput을 덮어쓰지 않도록
        var visual = new GameObject("Visual");
        visual.transform.SetParent(playerGo.transform, false);
        visual.AddComponent<SpriteRenderer>();
        rig.Animator = visual.AddComponent<Animator>();
        rig.Animator.runtimeAnimatorController = controller;
        rig.Anim = visual.AddComponent<PlayerAnimationController>();
        rig.Player = playerGo.AddComponent<PlayerController>();
        root.SetActive(true);
#endif
        return rig;
    }

    // ---------- Stretching (실제 Visual.controller) ----------

    [UnityTest]
    public IEnumerator Stretching_PlaysOnce_ThenIdleSouth()
    {
#if UNITY_EDITOR
        Rig rig = BuildRig();
        Assert.IsTrue(rig.Animator.HasState(0, PlayerAnimationController.StretchingStateHash), "Visual.controller에 Stretching State 필요");
        rig.Anim.PlayStretching();
        yield return null;
        yield return null;

        AnimatorStateInfo start = rig.Animator.GetCurrentAnimatorStateInfo(0);
        Assert.AreEqual(PlayerAnimationController.StretchingStateHash, start.fullPathHash, "Idle에 덮어써지지 않고 Stretching 재생");
        Assert.AreEqual(PlayerAnimationController.AnimState.Stretching, rig.Anim.CurrentState);
        Assert.IsFalse(start.loop, "Stretching Loop Time 꺼짐");
        Assert.Less(start.normalizedTime, 0.2f, "처음부터 재생");

        yield return WaitSeconds(1f);
        Assert.AreEqual(PlayerAnimationController.StretchingStateHash, rig.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash, "재생 중 유지");
        rig.Anim.PlayStretching(); // 재생 중 중복 요청 → 재시작하지 않음
        yield return null;
        Assert.Greater(rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 0.3f);

        float t0 = Time.time;
        while (rig.Anim.IsPlayingStretching && Time.time - t0 < 3f) yield return null;
        yield return null;
        Assert.IsFalse(rig.Anim.IsPlayingStretching, "1회 재생 후 종료");
        Assert.Less(Time.time - t0, 2.0f, "클립 길이(2.125초) 기준 종료, 보호 시간(3초) 아님");
        Assert.AreEqual(Hash("IdleSouth"), rig.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash, "종료 후 Idle");
        Assert.AreEqual(PlayerAnimationController.AnimState.Idle, rig.Anim.CurrentState);
#else
        Assert.Ignore("에디터 전용"); yield break;
#endif
    }

    [UnityTest]
    public IEnumerator Stretching_YieldsToRunImmediately()
    {
#if UNITY_EDITOR
        Rig rig = BuildRig();
        rig.Anim.PlayStretching();
        yield return null;
        yield return null;
        Assert.AreEqual(PlayerAnimationController.StretchingStateHash, rig.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash);

        rig.Router.SubmitInput(Vector2.right, false);
        yield return new WaitForFixedUpdate();
        yield return null;
        yield return null;

        Assert.IsFalse(rig.Anim.IsPlayingStretching);
        Assert.AreEqual(Hash("RunEast"), rig.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash, "이동하면 기존 Run 재생");
#else
        Assert.Ignore("에디터 전용"); yield break;
#endif
    }

    // ---------- 실제 Play 씬 ----------

    private static IEnumerator Load(System.Action load, string sceneName)
    {
        int loaded = 0;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> h = (s, _) => { if (s.name == sceneName) loaded++; };
        SceneManager.sceneLoaded += h;
        try
        {
            load();
            for (int i = 0; i < 300 && loaded == 0; i++) yield return null;
        }
        finally
        {
            SceneManager.sceneLoaded -= h;
        }
        Assert.AreEqual(1, loaded, $"{sceneName} 로드");
        yield return null; // Awake/Start 실행
    }

    private static StartCountdownController FindCountdownOrIgnore()
    {
        var countdown = Object.FindFirstObjectByType<StartCountdownController>();
        if (countdown == null)
        {
            Assert.Ignore("Play.unity에 StartCountdownController가 아직 없습니다. Inspector 설정 후 다시 실행하세요.");
        }
        return countdown;
    }

    /// <summary>카운트다운이 끝날 때까지 매 프레임 Ready 조건을 검사하며 메시지 순서를 기록한다.</summary>
    private static IEnumerator ObserveCountdown(List<string> shown, System.Action<float> onDone)
    {
        var gm = Object.FindFirstObjectByType<GameManager>();
        var timer = Object.FindFirstObjectByType<SurvivalTimer>();
        var spawner = Object.FindFirstObjectByType<MissileSpawner>();
        var player = Object.FindFirstObjectByType<PlayerController>();
        var messageUI = Object.FindFirstObjectByType<CenterMessageUI>();
        Assert.IsNotNull(messageUI, "UICanvas에 CenterMessageUI 필요");
        Vector2 startPos = player.GroundPosition;

        float t0 = Time.time;
        while (gm.CurrentState == GameState.Ready && Time.time - t0 < 8f)
        {
            Assert.AreEqual(0f, timer.ElapsedTime, "카운트다운 중 생존시간 0");
            Assert.IsFalse(spawner.IsSpawning, "카운트다운 중 생성 없음");
            Assert.AreEqual(0, spawner.ActiveMissileCount);
            Assert.IsFalse(player.CanControl, "카운트다운 중 조작 불가");
            Assert.AreEqual(startPos, player.GroundPosition);
            if (messageUI.IsVisible && (shown.Count == 0 || shown[shown.Count - 1] != messageUI.CurrentMessage))
            {
                shown.Add(messageUI.CurrentMessage);
            }
            yield return null;
        }
        onDone(Time.time - t0);
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.IsFalse(messageUI.IsVisible, "Playing: 메시지 숨김");
        Assert.IsTrue(timer.IsRunning);
        Assert.IsTrue(spawner.IsSpawning);
        Assert.Less(spawner.SpawnTimer, 0.2f, "첫 생성 대기는 Playing 시작 기준");
        Assert.IsTrue(player.CanControl);
    }

    [UnityTest]
    public IEnumerator PlayScene_Countdown_ThenPlaying_RestartRepeats_ExitLoadsMain()
    {
        yield return Load(SceneNavigator.LoadPlayScene, SceneNavigator.PlaySceneName);
        StartCountdownController countdown = FindCountdownOrIgnore();
        var anim = Object.FindFirstObjectByType<PlayerAnimationController>();
        Assert.IsTrue(countdown.IsRunning);
        if (anim != null)
        {
            yield return null;
            Assert.AreEqual(PlayerAnimationController.AnimState.Stretching, anim.CurrentState, "카운트다운 중 Stretching");
        }

        var shown = new List<string>();
        float duration = 0f;
        yield return ObserveCountdown(shown, d => duration = d);
        CollectionAssert.AreEqual(new[] { "3", "2", "1", "START!" }, shown);
        Assert.That(duration, Is.InRange(3.3f, 4.2f), "기본 3.6초");
        Assert.AreEqual(1, countdown.StartGameRequestCount);
        if (anim != null)
        {
            Assert.IsFalse(anim.IsPlayingStretching, "Stretching(2.125초)은 카운트다운 중 끝남");
            Assert.AreEqual(PlayerAnimationController.AnimState.Idle, anim.CurrentState);
        }

        // Restart: 카운트다운 처음부터
        yield return Load(() => new GameObject("Btn").AddComponent<SceneNavigationButtons>().OnClickRestart(), SceneNavigator.PlaySceneName);
        var newCountdown = Object.FindFirstObjectByType<StartCountdownController>();
        Assert.AreNotSame(countdown, newCountdown);
        Assert.IsTrue(newCountdown.IsRunning);
        Assert.AreEqual(GameState.Ready, Object.FindFirstObjectByType<GameManager>().CurrentState);
        Assert.AreEqual("3", Object.FindFirstObjectByType<CenterMessageUI>().CurrentMessage);

        // 카운트다운 중 Exit
        yield return WaitSeconds(1.2f);
        yield return Load(() => new GameObject("Btn").AddComponent<SceneNavigationButtons>().OnClickExit(), SceneNavigator.MainSceneName);
        Assert.AreEqual(SceneNavigator.MainSceneName, SceneManager.GetActiveScene().name);
        Assert.IsNull(Object.FindFirstObjectByType<GameManager>());
        Assert.AreEqual(0f, RecordStore.GetBestTime(), "카운트다운 중 Exit은 기록 없음");

        // 다시 Play 진입 → 카운트다운 처음부터
        yield return Load(SceneNavigator.LoadPlayScene, SceneNavigator.PlaySceneName);
        var again = new List<string>();
        yield return ObserveCountdown(again, _ => { });
        CollectionAssert.AreEqual(new[] { "3", "2", "1", "START!" }, again);
    }

    [UnityTest]
    public IEnumerator PlayScene_GameOverAfterCountdown_ShowsGameOverMessage()
    {
        yield return Load(SceneNavigator.LoadPlayScene, SceneNavigator.PlaySceneName);
        FindCountdownOrIgnore();
        yield return PlaySceneTestUtil.WaitForGameStart();

        var gm = Object.FindFirstObjectByType<GameManager>();
        var timer = Object.FindFirstObjectByType<SurvivalTimer>();
        var messageUI = Object.FindFirstObjectByType<CenterMessageUI>();
        var gameOverUI = Object.FindFirstObjectByType<GameOverUI>();
        Assert.IsNotNull(gameOverUI);
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.AreEqual(0f, RecordStore.GetBestTime(), "테스트 시작 시 저장된 기록 없음");

        var results = new List<GameResult>();
        gm.OnGameResultReady += r => results.Add(r);

        // 생존시간이 실제로 증가한 것을 확인한 뒤 게임 오버(0초 게임 오버로 신기록 판정이 흔들리지 않도록)
        float t0 = Time.time;
        while (timer.ElapsedTime < 0.3f && Time.time - t0 < 2f) yield return null;
        Assert.GreaterOrEqual(timer.ElapsedTime, 0.3f, "Playing 시작 후 생존시간 증가");
        gm.HandlePlayerHit(null);
        float survived = timer.ElapsedTime;
        Assert.AreEqual(survived, RecordStore.GetBestTime(), "기존 기록(0초)을 초과 → 즉시 저장");

        t0 = Time.time;
        while (results.Count == 0 && Time.time - t0 < 4f) yield return null;
        Assert.AreEqual(1, results.Count);
        Assert.IsTrue(results[0].IsNewRecord, "빈 기록에서 첫 게임 오버는 신기록");
        Assert.AreEqual(survived, results[0].SurvivalTime);
        yield return null;

        Assert.IsTrue(messageUI.IsVisible);
        Assert.AreSame(gameOverUI, messageUI.CurrentOwner, "카운트다운 메시지가 아닌 게임 오버 메시지");
        StringAssert.StartsWith("GAME OVER", messageUI.CurrentMessage);

        // 신기록 표시 방식은 GameOverUI 설정을 따른다:
        // New Record Object(예: TextPanel/BestIcon)가 연결되어 있으면 그 오브젝트를 켜고, 없으면 메시지 다음 줄에 문구를 붙인다.
        GameObject newRecordObject = null;
#if UNITY_EDITOR
        newRecordObject = new SerializedObject(gameOverUI).FindProperty("newRecordObject").objectReferenceValue as GameObject;
#endif
        if (newRecordObject != null)
        {
            Assert.IsTrue(newRecordObject.activeInHierarchy, $"신기록 표시 오브젝트({newRecordObject.name}) 활성");
            Assert.AreEqual("GAME OVER", messageUI.CurrentMessage, "전용 오브젝트가 있으면 문구를 중복으로 붙이지 않음");
        }
        else
        {
            StringAssert.Contains("NEW RECORD!", messageUI.CurrentMessage);
        }
    }
}
