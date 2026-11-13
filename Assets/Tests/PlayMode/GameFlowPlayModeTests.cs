using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 실제 Play.unity + 실제 MissileSpawner/Missile.prefab으로 시작 → 피격 → 게임 오버 흐름을 검증한다.
/// 씬에 GameManager/PlayerHitDetector가 아직 없으면 런타임에만 추가한다(씬 파일은 수정하지 않음).
/// </summary>
public class GameFlowPlayModeTests
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
    public IEnumerator GameManagerAwake_DisablesSpawnOnStart_SoOnlyGameManagerStartsSpawning()
    {
#if UNITY_EDITOR
        // 씬 로드 순서를 흉내: 같은 프레임에 비활성 오브젝트를 활성화 → 모든 Awake 후 Start
        var spawnerGo = new GameObject("Spawner");
        spawnerGo.SetActive(false);
        var spawner = spawnerGo.AddComponent<MissileSpawner>();
        Assert.IsTrue(spawner.SpawnOnStart);

        var gmGo = new GameObject("GM");
        gmGo.SetActive(false);
        var gm = gmGo.AddComponent<GameManager>();
        Wire(gm, "missileSpawner", spawner);
        var so = new SerializedObject(gm);
        so.FindProperty("startGameOnStart").boolValue = false; // Ready로 대기
        so.ApplyModifiedPropertiesWithoutUndo();

        spawnerGo.SetActive(true);
        gmGo.SetActive(true);
        yield return null;
        yield return null;

        Assert.IsFalse(spawner.SpawnOnStart);
        Assert.IsFalse(spawner.IsSpawning, "Ready 상태에서 Spawner가 스스로 시작하면 안 됨");
        Assert.AreEqual(GameState.Ready, gm.CurrentState);

        gm.StartGame();
        Assert.IsTrue(spawner.IsSpawning);
        Object.Destroy(gmGo);
        Object.Destroy(spawnerGo);
#else
        Assert.Ignore("에디터 전용 테스트");
        yield break;
#endif
    }

    [UnityTest]
    public IEnumerator FullFlow_MissileHitsIdlePlayer_GameOverFreezesEverything()
    {
#if UNITY_EDITOR
        Random.InitState(20261113);
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));

        var player = Object.FindFirstObjectByType<PlayerController>();
        var spawner = Object.FindFirstObjectByType<MissileSpawner>();
        Assert.IsNotNull(player);
        Assert.IsNotNull(spawner);

        var gm = Object.FindFirstObjectByType<GameManager>();
        var detector = player.GetComponent<PlayerHitDetector>();
        if (gm == null)
        {
            // 씬에 아직 연결되지 않은 경우 런타임에만 구성
            if (detector == null) detector = player.gameObject.AddComponent<PlayerHitDetector>();
            var gmGo = new GameObject("GameManager(Test)");
            gmGo.SetActive(false);
            gm = gmGo.AddComponent<GameManager>();
            Wire(gm, "playerController", player);
            Wire(gm, "playerHitDetector", detector);
            Wire(gm, "missileSpawner", spawner);
            Wire(detector, "gameManager", gm);
            gmGo.SetActive(true); // Awake/OnEnable → 다음 프레임 Start → StartGame
        }
        Assert.IsNotNull(detector);

        var states = new List<GameState>();
        gm.OnGameStateChanged += s => states.Add(s);
        int hitEvents = 0;
        detector.OnPlayerHit += _ => hitEvents++;

        yield return null;
        yield return null;
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.IsTrue(spawner.IsSpawning);
        Assert.IsTrue(player.CanControl);

        // 플레이어는 가만히 서 있고, 미사일이 맞힐 때까지 진행 (게임 시간 최대 180초)
        Time.timeScale = 5f;
        float t0 = Time.time;
        while (gm.CurrentState == GameState.Playing && Time.time - t0 < 180f) yield return null;
        float hitTime = Time.time - t0;
        Time.timeScale = 1f;

        Assert.AreEqual(GameState.GameOver, gm.CurrentState, "180초 안에 피격되지 않음");
        Assert.AreEqual(1, hitEvents);
        Assert.IsNotNull(gm.LastHitMissile);
        Assert.IsTrue(player.IsDead);
        Assert.IsFalse(player.CanControl);
        Assert.IsFalse(spawner.IsSpawning);
        Assert.AreEqual(1f, Time.timeScale);

        // 게임 오버 상태 유지 확인: 5초(생성 간격 1.8초 이상) 동안 미사일 정지, 신규 생성 없음, 플레이어 정지
        var frozen = new Dictionary<Missile, Vector3>();
        foreach (var m in spawner.ActiveMissiles)
        {
            Assert.IsFalse(m.IsMoving);
            frozen[m] = m.transform.position;
        }
        Vector2 playerPos = player.GroundPosition;
        int count = spawner.ActiveMissileCount;

        float t1 = Time.time;
        while (Time.time - t1 < 5f) yield return null;

        Assert.AreEqual(count, spawner.ActiveMissileCount, "게임 오버 후 미사일 수 변화 없음");
        foreach (var kv in frozen)
        {
            Assert.IsTrue(kv.Key != null, "정지한 미사일이 제거되면 안 됨");
            Assert.AreEqual(kv.Value, kv.Key.transform.position, "정지한 미사일 위치 고정");
        }
        Assert.AreEqual(playerPos, player.GroundPosition);
        Assert.AreEqual(1, hitEvents, "게임 오버 후 추가 피격 없음");
        // 런타임 생성 GM이면 [Playing, GameOver], 씬에 이미 있는 GM이면 [GameOver]
        Assert.AreEqual(1, states.FindAll(s => s == GameState.GameOver).Count, "GameOver 이벤트 1회");
        Assert.AreEqual(GameState.GameOver, states[states.Count - 1]);
        Assert.LessOrEqual(states.FindAll(s => s == GameState.Playing).Count, 1);

        Debug.Log($"[Stage4] full flow: GameOver at {hitTime:F2}s game time, frozen missiles={count}");
#else
        Assert.Ignore("에디터 전용 테스트");
        yield break;
#endif
    }
}
