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
/// Stage 7: 실제 Animator + Visual.controller로 State 전환, 재시작 없는 연속 재생, Death 완료 감지를 검증한다.
/// 후반부는 실제 Play 씬(PlayerAnimationController 부착 후)에서 사망 연출 중 Restart/Exit을 검증한다.
/// </summary>
public class PlayerAnimationPlayModeTests
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

    private static IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }

    private struct Rig
    {
        public PlayerController Player;
        public PlayerInputRouter Router;
        public Animator Animator;
        public PlayerAnimationController Anim;
        public Transform Visual;
    }

    /// <summary>씬과 같은 계층(Player/Visual: SpriteRenderer + Animator + PlayerAnimationController)을 런타임에 만든다.</summary>
    private Rig BuildRig()
    {
        var rig = new Rig();
#if UNITY_EDITOR
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        Assert.IsNotNull(controller, ControllerPath);

        root = new GameObject("AnimTestRoot");
        root.SetActive(false);
        var playerGo = new GameObject("Player");
        playerGo.transform.SetParent(root.transform, false);
        playerGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        playerGo.AddComponent<CircleCollider2D>().radius = 0.15f;
        rig.Router = playerGo.AddComponent<PlayerInputRouter>();
        rig.Router.enabled = false; // 실제 키보드 상태가 SubmitInput을 덮어쓰지 않도록
        var visual = new GameObject("Visual");
        visual.transform.SetParent(playerGo.transform, false);
        visual.AddComponent<SpriteRenderer>();
        rig.Animator = visual.AddComponent<Animator>();
        rig.Animator.runtimeAnimatorController = controller;
        rig.Anim = visual.AddComponent<PlayerAnimationController>();
        rig.Player = playerGo.AddComponent<PlayerController>();
        rig.Visual = visual.transform;
        root.SetActive(true);
#endif
        return rig;
    }

    private static int CurrentHash(Rig rig) => rig.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash;

    [UnityTest]
    public IEnumerator Start_IdleSouth_ContinuesWithoutRestart()
    {
#if UNITY_EDITOR
        Rig rig = BuildRig();
        yield return Frames(3);

        Assert.AreEqual(Hash("IdleSouth"), CurrentHash(rig));
        Assert.AreEqual(1f, rig.Animator.speed);
        Assert.IsFalse(rig.Animator.applyRootMotion);
        int requests = rig.Anim.PlayRequestCount;

        float t1 = rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        yield return WaitSeconds(0.5f);
        float t2 = rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;

        Assert.AreEqual(Hash("IdleSouth"), CurrentHash(rig));
        Assert.Greater(t2, t1 + 0.2f, "같은 State가 매 프레임 처음부터 재시작되지 않고 진행");
        Assert.AreEqual(requests, rig.Anim.PlayRequestCount);
        Assert.AreEqual(Vector3.zero, rig.Visual.localPosition, "애니메이션이 Visual 위치를 바꾸지 않음");
#else
        Assert.Ignore("에디터 전용"); yield break;
#endif
    }

    [UnityTest]
    public IEnumerator Run_DirectionChange_Stop_Jump()
    {
#if UNITY_EDITOR
        Rig rig = BuildRig();
        yield return Frames(2);

        rig.Router.SubmitInput(Vector2.right, false);
        yield return WaitSeconds(0.2f);
        Assert.AreEqual(Hash("RunEast"), CurrentHash(rig));
        float t1 = rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        yield return WaitSeconds(0.3f);
        Assert.Greater(rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, t1 + 0.1f, "RunEast 연속 재생");

        rig.Router.SubmitInput(new Vector2(1f, 1f), false);
        yield return WaitSeconds(0.1f);
        Assert.AreEqual(Hash("RunNorthEast"), CurrentHash(rig));

        rig.Router.SubmitInput(new Vector2(1f, 1f), true);
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        yield return Frames(2);
        Assert.IsFalse(rig.Player.IsGrounded);
        Assert.AreEqual(Hash("JumpNorthEast"), CurrentHash(rig));
        Assert.Greater(rig.Visual.localPosition.y, 0f, "점프 높이는 PlayerController가 Visual에 반영");

        float guard = Time.time;
        while (rig.Player.VerticalVelocity > 0f && Time.time - guard < 1f) yield return null;
        yield return Frames(2);
        Assert.AreEqual(Hash("FallNorthEast"), CurrentHash(rig));

        rig.Router.SubmitInput(Vector2.zero, false);
        guard = Time.time;
        while (!rig.Player.IsGrounded && Time.time - guard < 1.5f) yield return null;
        yield return Frames(2);
        Assert.AreEqual(Hash("IdleNorthEast"), CurrentHash(rig));
#else
        Assert.Ignore("에디터 전용"); yield break;
#endif
    }

    [UnityTest]
    public IEnumerator Death_PlaysDeath_FinishesOnceAfterClip()
    {
#if UNITY_EDITOR
        Rig rig = BuildRig();
        int finished = 0;
        rig.Anim.OnDeathFinished += () => finished++;
        yield return Frames(2);

        rig.Router.SubmitInput(Vector2.left, false);
        yield return WaitSeconds(0.2f);
        rig.Player.Die();
        float diedAt = Time.time;
        yield return Frames(2);
        Assert.AreEqual(Hash(PlayerAnimationController.DeathStateName), CurrentHash(rig), "방향과 무관하게 Death");

        while (finished == 0 && Time.time - diedAt < 3f) yield return null;
        float elapsed = Time.time - diedAt;
        Assert.AreEqual(1, finished);
        Assert.That(elapsed, Is.InRange(0.85f, 1.5f), "Death 클립 길이(1초) 후 완료");

        yield return WaitSeconds(1.5f);
        Assert.AreEqual(1, finished, "한 번만");
        Assert.AreEqual(Hash(PlayerAnimationController.DeathStateName), CurrentHash(rig), "Idle/Run으로 복귀하지 않음");
#else
        Assert.Ignore("에디터 전용"); yield break;
#endif
    }

    [UnityTest]
    public IEnumerator Death_AnimatorStalled_FallbackFinishes()
    {
#if UNITY_EDITOR
        Rig rig = BuildRig();
        int finished = 0;
        rig.Anim.OnDeathFinished += () => finished++;
        yield return Frames(2);

        rig.Animator.speed = 0f; // 테스트 전용: 재생이 진행되지 않는 상황
        rig.Player.Die();
        float diedAt = Time.time;
        while (finished == 0 && Time.time - diedAt < 4f) yield return null;

        Assert.AreEqual(1, finished, "완료를 확인할 수 없어도 대기가 끝남");
        Assert.That(Time.time - diedAt, Is.InRange(1.8f, 2.6f), "기본 보호 시간 2초");
#else
        Assert.Ignore("에디터 전용"); yield break;
#endif
    }

    [UnityTest]
    public IEnumerator Death_NoAnimatorController_FinishesImmediately()
    {
#if UNITY_EDITOR
        Rig rig = BuildRig();
        rig.Animator.runtimeAnimatorController = null;
        int finished = 0;
        rig.Anim.OnDeathFinished += () => finished++;
        yield return Frames(2);

        rig.Player.Die();
        yield return Frames(2);
        Assert.AreEqual(1, finished);
#else
        Assert.Ignore("에디터 전용"); yield break;
#endif
    }

    // ---------- 실제 Play 씬 (Inspector 설정 후) ----------

    private static IEnumerator LoadPlay(System.Action load = null)
    {
        int loaded = 0;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> h = (s, _) => { if (s.name == SceneNavigator.PlaySceneName) loaded++; };
        SceneManager.sceneLoaded += h;
        try
        {
            (load ?? SceneNavigator.LoadPlayScene)();
            for (int i = 0; i < 300 && loaded == 0; i++) yield return null;
        }
        finally
        {
            SceneManager.sceneLoaded -= h;
        }
        Assert.AreEqual(1, loaded);
        yield return Frames(3);
    }

    private static PlayerAnimationController FindAnimOrIgnore()
    {
        var anim = Object.FindFirstObjectByType<PlayerAnimationController>();
        if (anim == null)
        {
            Assert.Ignore("Play.unity의 Player/Visual에 PlayerAnimationController가 아직 없습니다. Stage 7 수동 설정 후 다시 실행하세요.");
        }
        return anim;
    }

    [UnityTest]
    public IEnumerator PlayScene_DeathThenResult_RestartDuringDeath_ResetsToIdleSouth()
    {
        yield return LoadPlay();
        PlayerAnimationController anim = FindAnimOrIgnore();
        var gm = Object.FindFirstObjectByType<GameManager>();
        Assert.AreEqual(Hash("IdleSouth"), anim.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash, "새 게임은 IdleSouth");

        var results = new List<GameResult>();
        gm.OnGameResultReady += r => results.Add(r);
        yield return WaitSeconds(0.5f);
        gm.HandlePlayerHit(null);
        float best = RecordStore.GetBestTime();
        Assert.Greater(best, 0.3f, "기록은 즉시 저장");
        yield return Frames(2);
        Assert.AreEqual(Hash(PlayerAnimationController.DeathStateName), anim.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash);
        Assert.AreEqual(0, results.Count, "사망 연출 중에는 결과 대기");
        Assert.IsTrue(gm.IsWaitingForDeathAnimation);

        // 사망 연출 도중 Restart
        yield return LoadPlay(() => new GameObject("Btn").AddComponent<SceneNavigationButtons>().OnClickRestart());
        var newGm = Object.FindFirstObjectByType<GameManager>();
        var newAnim = Object.FindFirstObjectByType<PlayerAnimationController>();
        Assert.AreNotSame(gm, newGm);
        Assert.AreEqual(GameState.Playing, newGm.CurrentState);
        Assert.AreEqual(Hash("IdleSouth"), newAnim.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash);
        Assert.AreEqual(PlayerAnimationController.AnimState.Idle, newAnim.CurrentState);
        Assert.IsFalse(newAnim.IsDeathFinished);

        var newResults = new List<GameResult>();
        newGm.OnGameResultReady += r => newResults.Add(r);
        yield return WaitSeconds(1.5f); // 이전 사망의 완료 시점(1초) 이후까지
        Assert.AreEqual(0, results.Count + newResults.Count, "이전 사망 이벤트가 새 게임에 영향 없음");
        Assert.AreEqual(GameState.Playing, newGm.CurrentState);
        Assert.AreEqual(best, RecordStore.GetBestTime(), "기록 유지");
    }

    [UnityTest]
    public IEnumerator PlayScene_DeathFinished_ThenResultPublished()
    {
        yield return LoadPlay();
        PlayerAnimationController anim = FindAnimOrIgnore();
        var gm = Object.FindFirstObjectByType<GameManager>();
        int finished = 0;
        anim.OnDeathFinished += () => finished++;
        var results = new List<GameResult>();
        gm.OnGameResultReady += r => results.Add(r);

        yield return WaitSeconds(0.3f);
        gm.HandlePlayerHit(null);
        float t0 = Time.time;
        while (results.Count == 0 && Time.time - t0 < 4f) yield return null;

        Assert.AreEqual(1, finished);
        Assert.AreEqual(1, results.Count);
        Assert.Greater(Time.time - t0, 0.8f, "사망 애니메이션 종료 후 전달");
        yield return WaitSeconds(1f);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(1, finished);
    }

    [UnityTest]
    public IEnumerator PlayScene_ExitDuringDeath_LoadsMain_RecordKept()
    {
        yield return LoadPlay();
        FindAnimOrIgnore();
        var gm = Object.FindFirstObjectByType<GameManager>();
        yield return WaitSeconds(0.5f);
        gm.HandlePlayerHit(null);
        float best = RecordStore.GetBestTime();
        yield return Frames(2);

        int loaded = 0;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> h = (s, _) => { if (s.name == SceneNavigator.MainSceneName) loaded++; };
        SceneManager.sceneLoaded += h;
        new GameObject("Btn").AddComponent<SceneNavigationButtons>().OnClickExit();
        for (int i = 0; i < 300 && loaded == 0; i++) yield return null;
        SceneManager.sceneLoaded -= h;

        Assert.AreEqual(1, loaded, "사망 연출 중 Exit 가능");
        Assert.AreEqual(best, RecordStore.GetBestTime());
        yield return WaitSeconds(1.5f);
        Assert.AreEqual(best, RecordStore.GetBestTime());
    }
}
