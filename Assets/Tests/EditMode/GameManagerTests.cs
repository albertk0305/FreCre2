using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GameManager 시작/종료 흐름 테스트 (EditMode).
/// EditMode에서는 Awake/OnEnable/Start가 호출되지 않으므로 StartGame()을 직접 호출한다.
/// Stage 5부터 SurvivalTimer/DifficultyManager까지 실제 씬과 같은 구성으로 연결해 Stage 4 동작 회귀도 함께 확인한다.
/// </summary>
public class GameManagerTests
{
    private const float Dt = 0.02f;
    private static readonly Vector2 Center = new Vector2(0.5f, -0.5f);

    private GameObject arenaGo, cameraGo, playerGo, templateGo, spawnerGo, managerGo, systemsGo;
    private PlayerController player;
    private PlayerInputRouter router;
    private PlayerHitDetector detector;
    private MissileSpawner spawner;
    private SurvivalTimer timer;
    private DifficultyManager dm;
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

        systemsGo = new GameObject("GameSystems");
        timer = systemsGo.AddComponent<SurvivalTimer>();
        dm = systemsGo.AddComponent<DifficultyManager>();
        SetRef(dm, "survivalTimer", timer);

        spawnerGo = new GameObject("MissileSpawner");
        spawner = spawnerGo.AddComponent<MissileSpawner>();
        SetRef(spawner, "missilePrefab", template);
        SetRef(spawner, "arenaBounds", arena);
        SetRef(spawner, "targetCamera", cam);
        SetRef(spawner, "difficultyManager", dm);

        managerGo = new GameObject("GameManager");
        gm = managerGo.AddComponent<GameManager>();
        SetRef(gm, "playerController", player);
        SetRef(gm, "playerHitDetector", detector);
        SetRef(gm, "missileSpawner", spawner);
        SetRef(gm, "survivalTimer", timer);
        SetRef(detector, "gameManager", gm);

        events.Clear();
        gm.OnGameStateChanged += s => events.Add(s);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(managerGo);
        Object.DestroyImmediate(spawnerGo);
        Object.DestroyImmediate(systemsGo);
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

    // ---------- Stage 5: 생존시간 ----------

    /// <summary>한 프레임: SurvivalTimer(실행 순서 -50) → MissileSpawner.</summary>
    private void Frame(float dt)
    {
        timer.Tick(dt);
        spawner.Tick(dt);
    }

    [Test]
    public void BeforeStart_TimerNotRunning()
    {
        Assert.IsFalse(timer.IsRunning);
        timer.Tick(1f);
        Assert.AreEqual(0f, timer.ElapsedTime);
    }

    [Test]
    public void StartGame_ResetsAndStartsTimer()
    {
        timer.StartTimer();
        timer.Tick(42f); // 이전 값이 남아 있어도
        timer.StopTimer();

        gm.StartGame();
        Assert.IsTrue(timer.IsRunning);
        Assert.AreEqual(0f, timer.ElapsedTime);

        Frame(Dt);
        Assert.AreEqual(Dt, timer.ElapsedTime, 1e-6f);
    }

    [Test]
    public void StartGame_FirstMissileAfterInitialInterval()
    {
        timer.StartTimer();
        timer.Tick(500f); // 이전 판의 긴 생존시간이 남아 있어도 0초 난이도로 시작
        gm.StartGame();
        Assert.AreEqual(1.80f, spawner.SpawnInterval, 1e-6f);
        Assert.AreEqual(5, spawner.MaxActiveMissiles);

        for (int i = 0; i < 89; i++) Frame(Dt);
        Assert.AreEqual(0, spawner.ActiveMissileCount);
        Frame(0.03f);
        Assert.AreEqual(1, spawner.ActiveMissileCount);
    }

    [Test]
    public void Playing_TimerDrivesDifficulty()
    {
        gm.StartGame();
        for (int i = 0; i < 3250; i++) Frame(Dt); // 65초
        Assert.AreEqual(65f, timer.ElapsedTime, 0.05f);
        // 마지막 생성 시도는 최근 1간격(≈1초) 안에서 일어났으므로 64~65초 난이도가 적용되어 있다.
        Assert.That(spawner.SpawnInterval, Is.InRange(0.98f, 1.01f), "1.80 - t×0.0125 (t≈64~65)");
        Assert.AreEqual(9, spawner.MaxActiveMissiles, "60~74초 상한 9");
        Assert.AreEqual(9, spawner.ActiveMissileCount, "EditMode에선 제거되지 않으므로 상한까지 참");
    }

    [Test]
    public void GameOver_StopsTimer_AndTimeDoesNotIncrease()
    {
        gm.StartGame();
        for (int i = 0; i < 100; i++) Frame(Dt); // 2초
        HitPlayer();

        Assert.IsFalse(timer.IsRunning);
        float final = timer.ElapsedTime;
        Assert.AreEqual(2f, final, 1e-3f);

        int count = spawner.ActiveMissileCount;
        for (int i = 0; i < 500; i++) Frame(Dt);
        Assert.AreEqual(final, timer.ElapsedTime, "게임 오버 후 시간 증가 없음");
        Assert.AreEqual(count, spawner.ActiveMissileCount, "게임 오버 후 신규 생성 없음");
        foreach (Missile m in spawner.ActiveMissiles) Assert.IsFalse(m.IsMoving);
    }

    [Test]
    public void GameOver_Twice_TimerValueKept()
    {
        gm.StartGame();
        Frame(1f);
        HitPlayer();
        float final = timer.ElapsedTime;

        gm.EndGame();
        gm.HandlePlayerHit(null);
        Frame(1f);

        Assert.AreEqual(final, timer.ElapsedTime);
        Assert.IsFalse(timer.IsRunning);
        CollectionAssert.AreEqual(new[] { GameState.Playing, GameState.GameOver }, events);
    }

    [Test]
    public void Restart_AfterGameOver_TimerFromZero_DifficultyReset()
    {
        gm.StartGame();
        for (int i = 0; i < 3000; i++) Frame(Dt); // 60초
        foreach (Missile m in new List<Missile>(spawner.ActiveMissiles)) m.Despawn(); // HitPlayer용 빈자리
        HitPlayer();
        Assert.Greater(timer.ElapsedTime, 59f);

        gm.StartGame();
        Assert.IsTrue(timer.IsRunning);
        Assert.AreEqual(0f, timer.ElapsedTime);
        Assert.AreEqual(1.80f, spawner.SpawnInterval, 1e-6f, "재시작 시 0초 난이도");
        Assert.AreEqual(5, spawner.MaxActiveMissiles);
    }

    [Test]
    public void StartGame_WhilePlaying_DoesNotResetTimer()
    {
        gm.StartGame();
        Frame(3f);
        gm.StartGame();
        Assert.AreEqual(3f, timer.ElapsedTime, 1e-6f);
    }

    [Test]
    public void WithoutSurvivalTimer_Stage4FlowStillWorks()
    {
        SetRef(gm, "survivalTimer", null);
        SetRef(spawner, "difficultyManager", null);

        gm.StartGame();
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.IsFalse(timer.IsRunning);
        spawner.Tick(1.80f);
        Assert.AreEqual(1, spawner.ActiveMissileCount);
        HitPlayer();
        Assert.AreEqual(GameState.GameOver, gm.CurrentState);
    }
}
