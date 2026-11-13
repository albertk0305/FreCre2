using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Stage 6: 실제 Main/Play 씬과 SceneManager로 씬 전환, 재시작 초기화, 최고기록 저장 정책을 검증한다.
/// SceneManager.LoadScene(name)을 쓰므로 두 씬이 Build Profiles Scene List에 있어야 한다.
/// 피격 위험이 없는 시간(첫 미사일 1.8초 생성 + 비행 ≥ 약 1.9초) 안에서만 진행 상태를 만든다.
/// </summary>
public class SceneNavigationPlayModeTests
{
    private BestTimeBackup bestTimeBackup;

    [SetUp]
    public void SetUp()
    {
        bestTimeBackup = BestTimeBackup.Capture();
        BestTimeBackup.ClearForTest();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Time.timeScale = 1f;
        bestTimeBackup.Restore();
        yield return null;
    }

    /// <summary>
    /// load를 실행하고 sceneName 씬이 새로 로드될 때까지 기다린다(같은 씬 재로드도 감지).
    /// 이후 Start() → GameManager.StartGame()이 실행되도록 몇 프레임 더 진행한다.
    /// </summary>
    private static IEnumerator LoadAndWait(System.Action load, string sceneName)
    {
        int loaded = 0;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> handler = (scene, _) =>
        {
            if (scene.name == sceneName) loaded++;
        };
        SceneManager.sceneLoaded += handler;
        try
        {
            load();
            for (int i = 0; i < 300 && loaded == 0; i++)
            {
                yield return null;
            }
        }
        finally
        {
            SceneManager.sceneLoaded -= handler;
        }
        Assert.AreEqual(1, loaded, $"{sceneName} 씬 로드");
        Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name);
        yield return null;
        yield return null;
    }

    private static IEnumerator WaitSeconds(float seconds)
    {
        float t0 = Time.time;
        while (Time.time - t0 < seconds) yield return null;
    }

    private static SceneNavigationButtons CreateButtons()
    {
        return new GameObject("Buttons(Test)").AddComponent<SceneNavigationButtons>();
    }

    private static T Find<T>() where T : Object
    {
        T found = Object.FindFirstObjectByType<T>();
        Assert.IsNotNull(found, typeof(T).Name);
        return found;
    }

    private static void AssertFreshGame(GameManager gm)
    {
        var timer = Find<SurvivalTimer>();
        var spawner = Find<MissileSpawner>();
        var player = Find<PlayerController>();
        var detector = Find<PlayerHitDetector>();

        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.IsFalse(gm.HasGameResult);
        Assert.IsTrue(timer.IsRunning);
        Assert.Less(timer.ElapsedTime, 0.2f, "생존시간 0초부터");
        Assert.IsTrue(spawner.IsSpawning);
        Assert.Less(spawner.SpawnTimer, 0.2f, "생성 타이머 초기화");
        Assert.AreEqual(1.80f, spawner.SpawnInterval, 1e-5f, "난이도 0초 기준");
        Assert.AreEqual(5, spawner.MaxActiveMissiles);
        Assert.AreEqual(0, spawner.ActiveMissileCount);
        Assert.AreEqual(0, Object.FindObjectsByType<Missile>(FindObjectsSortMode.None).Length, "미사일 오브젝트 없음");
        Assert.IsFalse(player.IsDead);
        Assert.IsTrue(player.CanControl);
        Assert.IsTrue(player.IsGrounded);
        Assert.AreEqual(0f, player.JumpHeight);
        Assert.IsFalse(detector.IsHitProcessed);
    }

    [UnityTest]
    public IEnumerator Main_PlayButton_LoadsPlay_AndStartsNewGame()
    {
        yield return LoadAndWait(SceneNavigator.LoadMainScene, SceneNavigator.MainSceneName);
        Assert.IsNull(Object.FindFirstObjectByType<GameManager>(), "Main에는 게임 없음");

        yield return LoadAndWait(() => CreateButtons().OnClickPlay(), SceneNavigator.PlaySceneName);
        AssertFreshGame(Find<GameManager>());
    }

    [UnityTest]
    public IEnumerator Play_ExitDuringPlay_LoadsMain_DoesNotSaveRecord()
    {
        RecordStore.TryUpdateRecord(0.5f);
        yield return LoadAndWait(SceneNavigator.LoadPlayScene, SceneNavigator.PlaySceneName);

        var gm = Find<GameManager>();
        var states = new List<GameState>();
        gm.OnGameStateChanged += s => states.Add(s);
        yield return WaitSeconds(1.5f);
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.Greater(Find<SurvivalTimer>().ElapsedTime, 1f, "현재 생존시간이 기존 기록(0.5초)보다 큼");

        yield return LoadAndWait(() => CreateButtons().OnClickExit(), SceneNavigator.MainSceneName);

        Assert.AreEqual(0.5f, RecordStore.GetBestTime(), "진행 중 Exit은 기록하지 않음");
        Assert.AreEqual(0, states.Count, "Exit이 게임 오버를 발생시키지 않음");
    }

    [UnityTest]
    public IEnumerator Play_RestartDuringPlay_ResetsEverything_DoesNotSaveRecord()
    {
        RecordStore.TryUpdateRecord(0.5f);
        yield return LoadAndWait(SceneNavigator.LoadPlayScene, SceneNavigator.PlaySceneName);

        var oldGm = Find<GameManager>();
        var oldPlayer = Find<PlayerController>();
        Vector2 startPosition = oldPlayer.GroundPosition;
        var states = new List<GameState>();
        oldGm.OnGameStateChanged += s => states.Add(s);

        yield return WaitSeconds(2.0f); // 첫 미사일 생성(1.8초) 이후, 도달 이전
        Assert.GreaterOrEqual(Find<MissileSpawner>().ActiveMissileCount, 1, "재시작 전 미사일 존재");
        Assert.IsTrue(oldPlayer.TryJump(), "재시작 직전 점프 상태");
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        Assert.Greater(oldPlayer.JumpHeight, 0f);

        yield return LoadAndWait(() => CreateButtons().OnClickRestart(), SceneNavigator.PlaySceneName);

        var gm = Find<GameManager>();
        Assert.AreNotSame(oldGm, gm, "Play 씬이 다시 로드됨");
        Assert.IsTrue(oldGm == null, "이전 씬 오브젝트 파괴");
        AssertFreshGame(gm);
        Assert.AreEqual(startPosition.x, Find<PlayerController>().GroundPosition.x, 1e-4f, "플레이어 시작 위치");
        Assert.AreEqual(startPosition.y, Find<PlayerController>().GroundPosition.y, 1e-4f);

        Assert.AreEqual(0.5f, RecordStore.GetBestTime(), "진행 중 Restart는 기록하지 않음");
        Assert.AreEqual(0, states.Count, "Restart가 게임 오버를 발생시키지 않음");
    }

    [UnityTest]
    public IEnumerator GameOver_SavesRecord_KeptThroughRestartExitAndPlay()
    {
        yield return LoadAndWait(SceneNavigator.LoadPlayScene, SceneNavigator.PlaySceneName);
        var gm = Find<GameManager>();
        var results = new List<GameResult>();
        gm.OnGameResultReady += r => results.Add(r);

        yield return WaitSeconds(1.0f);
        gm.HandlePlayerHit(null); // 미사일 피격과 같은 경로로 게임 오버
        float survived = Find<SurvivalTimer>().ElapsedTime;

        Assert.AreEqual(GameState.GameOver, gm.CurrentState);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(survived, results[0].SurvivalTime);
        Assert.AreEqual(survived, results[0].BestTime);
        Assert.IsTrue(results[0].IsNewRecord);
        Assert.AreEqual(survived, RecordStore.GetBestTime());

        // 게임 오버 이후 버튼: 이미 저장된 기록 유지
        yield return LoadAndWait(() => CreateButtons().OnClickRestart(), SceneNavigator.PlaySceneName);
        AssertFreshGame(Find<GameManager>());
        Assert.AreEqual(survived, RecordStore.GetBestTime(), "Restart 후 유지");

        yield return LoadAndWait(() => CreateButtons().OnClickExit(), SceneNavigator.MainSceneName);
        Assert.AreEqual(survived, RecordStore.GetBestTime(), "Main 이동 후 유지");

        yield return LoadAndWait(() => CreateButtons().OnClickPlay(), SceneNavigator.PlaySceneName);
        Assert.AreEqual(survived, RecordStore.GetBestTime(), "다시 Play 진입 후 유지");

        // 더 짧은 기록으로 게임 오버 → 신기록 아님, 기록 유지
        var gm2 = Find<GameManager>();
        gm2.HandlePlayerHit(null);
        Assert.IsFalse(gm2.LastGameResult.IsNewRecord);
        Assert.AreEqual(survived, gm2.LastGameResult.BestTime);
        Assert.AreEqual(survived, RecordStore.GetBestTime());
    }

    [UnityTest]
    public IEnumerator GameOver_ThenExit_LoadsMain_RecordKept()
    {
        yield return LoadAndWait(SceneNavigator.LoadPlayScene, SceneNavigator.PlaySceneName);
        yield return WaitSeconds(0.5f);
        var gm = Find<GameManager>();
        gm.HandlePlayerHit(null);
        float best = RecordStore.GetBestTime();
        Assert.Greater(best, 0.4f);

        yield return LoadAndWait(() => CreateButtons().OnClickExit(), SceneNavigator.MainSceneName);
        Assert.AreEqual(best, RecordStore.GetBestTime());
    }
}
