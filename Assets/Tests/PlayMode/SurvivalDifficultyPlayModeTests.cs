using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Stage 5: 실제 프레임(Time.deltaTime)에서 SurvivalTimer가 Playing 동안만 증가하고 게임 오버 시 즉시 멈추는지,
/// 그리고 MissileSpawner가 DifficultyManager의 스냅샷을 받는지 검증한다.
/// </summary>
public class SurvivalDifficultyPlayModeTests
{
    private const string ScenePath = "Assets/Scenes/Play.unity";

    private BestTimeBackup bestTimeBackup;

    [SetUp]
    public void BackupBestTime()
    {
        // Stage 6: 게임 오버 시 최고기록이 저장되므로 실제 기록을 백업한다.
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

#if UNITY_EDITOR
    private static void Wire(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
#endif

    [UnityTest]
    public IEnumerator Timer_CountsRealFramesWhilePlaying_StopsOnGameOver()
    {
#if UNITY_EDITOR
        var root = new GameObject("Stage5Test");
        root.SetActive(false);
        var timer = root.AddComponent<SurvivalTimer>();
        var dm = root.AddComponent<DifficultyManager>();
        Wire(dm, "survivalTimer", timer);

        var spawnerGo = new GameObject("Spawner");
        spawnerGo.transform.SetParent(root.transform);
        var spawner = spawnerGo.AddComponent<MissileSpawner>(); // 프리팹 없음: 생성 없이 난이도 적용만 확인
        Wire(spawner, "difficultyManager", dm);

        var gmGo = new GameObject("GM");
        gmGo.transform.SetParent(root.transform);
        var gm = gmGo.AddComponent<GameManager>();
        Wire(gm, "missileSpawner", spawner);
        Wire(gm, "survivalTimer", timer);

        root.SetActive(true); // Awake → 다음 프레임 Start → StartGame
        yield return null;

        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.IsTrue(timer.IsRunning);
        Assert.IsTrue(spawner.IsSpawning);
        // 테스트가 직접 만든 DifficultyManager(코드 기본값) 기준. 씬 밸런스와 무관한 명시적 테스트 데이터.
        Assert.AreEqual(DifficultyManager.DefaultInitialSpawnInterval, spawner.SpawnInterval, 1e-5f);

        float startElapsed = timer.ElapsedTime;
        float t0 = Time.time;
        while (Time.time - t0 < 1.0f) yield return null;
        float measured = timer.ElapsedTime - startElapsed;
        Assert.AreEqual(Time.time - t0, measured, 0.05f, "Time.deltaTime 누적");

        gm.EndGame();
        float final = timer.ElapsedTime;
        Assert.IsFalse(timer.IsRunning);
        Assert.IsFalse(spawner.IsSpawning);

        float t1 = Time.time;
        while (Time.time - t1 < 0.5f) yield return null;
        Assert.AreEqual(final, timer.ElapsedTime, "게임 오버 후 시간 증가 없음");
        Assert.AreEqual(1f, Time.timeScale, "timeScale 미사용");

        Object.Destroy(root);
#else
        Assert.Ignore("에디터 전용 테스트");
        yield break;
#endif
    }

    [UnityTest]
    public IEnumerator PlayScene_WhenStage5Wired_TimerAndDifficultyConnected()
    {
#if UNITY_EDITOR
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        yield return null;
        yield return PlaySceneTestUtil.WaitForGameStart(); // 시작 카운트다운이 있으면 Playing까지 대기

        var timer = Object.FindFirstObjectByType<SurvivalTimer>();
        var dm = Object.FindFirstObjectByType<DifficultyManager>();
        var spawner = Object.FindFirstObjectByType<MissileSpawner>();
        var gm = Object.FindFirstObjectByType<GameManager>();
        if (timer == null || dm == null)
        {
            Assert.Ignore("Play.unity에 SurvivalTimer/DifficultyManager가 아직 없습니다. Stage 5 수동 설정 후 다시 실행하세요.");
        }

        Assert.IsNotNull(gm);
        Assert.IsNotNull(spawner);
        Assert.AreSame(timer, dm.SurvivalTimer, "DifficultyManager.Survival Timer 참조");
        Assert.AreSame(dm, spawner.DifficultyManager, "MissileSpawner.Difficulty Manager 참조");
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.IsTrue(timer.IsRunning, "GameManager.Survival Timer 참조 (StartGame에서 StartTimer)");
        Assert.Less(timer.ElapsedTime, 1f);

        float t0 = Time.time;
        while (Time.time - t0 < 0.5f && gm.CurrentState == GameState.Playing) yield return null;
        Assert.Greater(timer.ElapsedTime, 0.4f);

        gm.EndGame();
        float final = timer.ElapsedTime;
        yield return null;
        yield return null;
        Assert.AreEqual(final, timer.ElapsedTime);
        Assert.IsFalse(spawner.IsSpawning);
#else
        Assert.Ignore("에디터 전용 테스트");
        yield break;
#endif
    }
}
