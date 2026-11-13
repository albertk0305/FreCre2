using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GameManager 시작/종료 흐름 테스트 (EditMode).
/// EditMode에서는 Awake/OnEnable/Start가 호출되지 않으므로 StartGame()을 직접 호출한다.
/// </summary>
public class GameManagerTests
{
    private const float Dt = 0.02f;
    private static readonly Vector2 Center = new Vector2(0.5f, -0.5f);

    private GameObject arenaGo, cameraGo, playerGo, templateGo, spawnerGo, managerGo;
    private PlayerController player;
    private PlayerInputRouter router;
    private PlayerHitDetector detector;
    private MissileSpawner spawner;
    private GameManager gm;
    private readonly List<GameState> events = new List<GameState>();
    private Random.State savedRandom;

    [SetUp]
    public void SetUp()
    {
        savedRandom = Random.state;
        Random.InitState(777);

        arenaGo = new GameObject("Arena");
        var arena = arenaGo.AddComponent<ArenaBounds>();
        arena.Center = Center;
        arena.HalfSize = 2f;

        cameraGo = new GameObject("Cam");
        var cam = cameraGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 2.5f;
        cameraGo.transform.position = new Vector3(0.5f, -0.5f, -10f);

        playerGo = new GameObject("Player");
        playerGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        var col = playerGo.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.15f;
        router = playerGo.AddComponent<PlayerInputRouter>();
        player = playerGo.AddComponent<PlayerController>();
        SetRef(player, "arenaBounds", arena);
        detector = playerGo.AddComponent<PlayerHitDetector>();
        SetRef(detector, "playerController", player);
        player.ResetPlayer(Center);

        templateGo = new GameObject("MissileTemplate");
        templateGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        templateGo.AddComponent<CircleCollider2D>().isTrigger = true;
        var template = templateGo.AddComponent<Missile>();

        spawnerGo = new GameObject("MissileSpawner");
        spawner = spawnerGo.AddComponent<MissileSpawner>();
        SetRef(spawner, "missilePrefab", template);
        SetRef(spawner, "arenaBounds", arena);
        SetRef(spawner, "targetCamera", cam);

        managerGo = new GameObject("GameManager");
        gm = managerGo.AddComponent<GameManager>();
        SetRef(gm, "playerController", player);
        SetRef(gm, "playerHitDetector", detector);
        SetRef(gm, "missileSpawner", spawner);
        SetRef(detector, "gameManager", gm);

        events.Clear();
        gm.OnGameStateChanged += s => events.Add(s);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(managerGo);
        Object.DestroyImmediate(spawnerGo);
        Object.DestroyImmediate(templateGo);
        Object.DestroyImmediate(playerGo);
        Object.DestroyImmediate(cameraGo);
        Object.DestroyImmediate(arenaGo);
        Random.state = savedRandom;
    }

    private static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private Missile HitPlayer()
    {
        Missile m = spawner.SpawnMissile();
        Assert.IsNotNull(m);
        Assert.IsTrue(detector.CheckMissileCollision(m));
        return m;
    }

    // ---------- 시작 ----------

    [Test]
    public void InitialState_IsReady()
    {
        Assert.AreEqual(GameState.Ready, gm.CurrentState);
    }

    [Test]
    public void StartGame_SwitchesToPlaying()
    {
        gm.StartGame();
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        CollectionAssert.AreEqual(new[] { GameState.Playing }, events);
    }

    [Test]
    public void StartGame_EnablesPlayerControl()
    {
        player.SetControlEnabled(false);
        router.SetInputEnabled(false);
        gm.StartGame();
        Assert.IsTrue(player.CanControl);
        Assert.IsTrue(router.InputEnabled, "Router는 PlayerController와 같은 오브젝트에서 자동으로 찾음");
    }

    [Test]
    public void StartGame_StartsSpawning()
    {
        Assert.IsFalse(spawner.IsSpawning);
        gm.StartGame();
        Assert.IsTrue(spawner.IsSpawning);
        spawner.Tick(1.80f);
        Assert.AreEqual(1, spawner.ActiveMissileCount);
    }

    [Test]
    public void StartGame_ResetsHitState()
    {
        // GameManager 연결을 잠시 끊고 피격 플래그를 켠다
        SetRef(detector, "gameManager", null);
        Assert.IsTrue(detector.CheckMissileCollision(spawner.SpawnMissile()));
        Assert.IsTrue(detector.IsHitProcessed);
        SetRef(detector, "gameManager", gm);

        gm.StartGame();
        Assert.IsFalse(detector.IsHitProcessed, "StartGame이 피격 상태를 초기화");
        HitPlayer();
        Assert.IsTrue(detector.IsHitProcessed);
    }

    [Test]
    public void StartGame_CalledTwice_NoDuplicateEventsOrSubscriptions()
    {
        gm.StartGame();
        spawner.Tick(1.0f);
        gm.StartGame();
        gm.StartGame();

        CollectionAssert.AreEqual(new[] { GameState.Playing }, events);
        Assert.AreEqual(1.0f, spawner.SpawnTimer, 1e-6f, "중복 시작으로 생성 타이머가 리셋되지 않음");

        HitPlayer(); // 구독이 중복되면 GameOver 이벤트가 두 번 나올 수 있음
        CollectionAssert.AreEqual(new[] { GameState.Playing, GameState.GameOver }, events);
    }

    // ---------- 피격 / 게임 오버 ----------

    [Test]
    public void PlayerHit_SwitchesToGameOver()
    {
        gm.StartGame();
        Missile m = HitPlayer();
        Assert.AreEqual(GameState.GameOver, gm.CurrentState);
        Assert.AreSame(m, gm.LastHitMissile);
    }

    [Test]
    public void GameOver_StopsPlayerMoveAndJump()
    {
        gm.StartGame();
        HitPlayer();

        Assert.IsTrue(player.IsDead);
        Assert.IsFalse(player.CanControl);
        Assert.IsFalse(router.InputEnabled);

        Vector2 before = player.GroundPosition;
        router.SubmitInput(Vector2.right, true); // 차단된 입력
        player.SetControlEnabled(true);           // 사망 중에는 다시 켤 수 없음
        for (int i = 0; i < 20; i++) player.Tick(Dt);

        Assert.AreEqual(before, player.GroundPosition);
        Assert.IsTrue(player.IsGrounded);
        Assert.IsFalse(player.TryJump());
    }

    [Test]
    public void GameOver_StopsNewSpawns()
    {
        gm.StartGame();
        HitPlayer();
        int count = spawner.ActiveMissileCount;

        Assert.IsFalse(spawner.IsSpawning);
        for (int i = 0; i < 10; i++) spawner.Tick(1.80f);
        Assert.AreEqual(count, spawner.ActiveMissileCount);
    }

    [Test]
    public void GameOver_StopsExistingMissiles()
    {
        gm.StartGame();
        var a = spawner.SpawnMissile();
        var b = spawner.SpawnMissile();
        Assert.IsTrue(a.IsMoving && b.IsMoving);

        Assert.IsTrue(detector.CheckMissileCollision(a));

        foreach (var m in spawner.ActiveMissiles)
        {
            Assert.IsFalse(m.IsMoving);
            Vector2 p = m.Position;
            m.Tick(0.5f);
            Assert.AreEqual(p, m.Position);
            Assert.IsFalse(m.IsDespawned, "정지할 뿐 제거하지 않음");
        }
        Assert.AreEqual(2, spawner.ActiveMissileCount);
    }

    [Test]
    public void GameOver_NotProcessedTwice()
    {
        gm.StartGame();
        Missile first = HitPlayer();
        Missile other = spawner.SpawnMissile(); // 생성 중지 상태라도 SpawnMissile 직접 호출은 가능
        gm.HandlePlayerHit(other);
        gm.EndGame();

        CollectionAssert.AreEqual(new[] { GameState.Playing, GameState.GameOver }, events);
        Assert.AreSame(first, gm.LastHitMissile);
    }

    [Test]
    public void HandlePlayerHit_IgnoredWhenNotPlaying()
    {
        gm.HandlePlayerHit(null);
        Assert.AreEqual(GameState.Ready, gm.CurrentState);
        Assert.AreEqual(0, events.Count);
        Assert.IsFalse(player.IsDead);
    }

    [Test]
    public void EndGame_DoesNotChangeTimeScale()
    {
        gm.StartGame();
        HitPlayer();
        Assert.AreEqual(1f, Time.timeScale);
    }

    [Test]
    public void StateEvents_InOrder()
    {
        gm.StartGame();
        HitPlayer();
        CollectionAssert.AreEqual(new[] { GameState.Playing, GameState.GameOver }, events);
    }

    [Test]
    public void StartGame_AfterGameOver_RestartsCleanly()
    {
        gm.StartGame();
        HitPlayer();

        gm.StartGame();
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.IsFalse(player.IsDead);
        Assert.IsTrue(player.CanControl);
        Assert.IsTrue(router.InputEnabled);
        Assert.IsFalse(detector.IsHitProcessed);
        Assert.IsTrue(spawner.IsSpawning);
        CollectionAssert.AreEqual(new[] { GameState.Playing, GameState.GameOver, GameState.Playing }, events);
    }
}
