using System.Collections.Generic;
using System.Linq;
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
    private BestTimeBackup bestTimeBackup;
    private readonly List<GameResult> results = new List<GameResult>();

    [SetUp]
    public void SetUp()
    {
        // Stage 6: 게임 오버 시 최고기록을 저장하므로 실제 기록을 백업하고 빈 상태에서 시작한다.
        bestTimeBackup = BestTimeBackup.Capture();
        BestTimeBackup.ClearForTest();

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
        results.Clear();
        gm.OnGameResultReady += r => results.Add(r);
    }

    [TearDown]
    public void TearDown()
    {
        if (managerGo != null) Object.DestroyImmediate(managerGo);
        Object.DestroyImmediate(spawnerGo);
        Object.DestroyImmediate(systemsGo);
        Object.DestroyImmediate(templateGo);
        Object.DestroyImmediate(playerGo);
        Object.DestroyImmediate(cameraGo);
        Object.DestroyImmediate(arenaGo);
        Random.state = savedRandom;
        bestTimeBackup.Restore();
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

    // ---------- Stage 6: 최고기록 / GameResult ----------

    [Test]
    public void MissileHitGameOver_SavesRecord_AndBuildsResult()
    {
        gm.StartGame();
        Frame(2f);
        Assert.IsFalse(gm.HasGameResult);
        HitPlayer();

        Assert.IsTrue(gm.HasGameResult);
        GameResult r = gm.LastGameResult;
        Assert.AreEqual(2f, r.SurvivalTime, 1e-6f);
        Assert.AreEqual(timer.ElapsedTime, r.SurvivalTime, "최종 생존시간 = SurvivalTimer.ElapsedTime");
        Assert.AreEqual(2f, r.BestTime, 1e-6f);
        Assert.IsTrue(r.IsNewRecord);
        Assert.AreEqual(2f, RecordStore.GetBestTime(), 1e-6f, "PlayerPrefs에 저장됨");
    }

    [Test]
    public void LowerThanBest_NotNewRecord_BestKept()
    {
        RecordStore.TryUpdateRecord(100f);
        gm.StartGame();
        Frame(2f);
        HitPlayer();

        GameResult r = gm.LastGameResult;
        Assert.AreEqual(2f, r.SurvivalTime, 1e-6f);
        Assert.AreEqual(100f, r.BestTime);
        Assert.IsFalse(r.IsNewRecord);
        Assert.AreEqual(100f, RecordStore.GetBestTime());
    }

    [Test]
    public void EqualToBest_NotNewRecord()
    {
        RecordStore.TryUpdateRecord(2f);
        gm.StartGame();
        Frame(2f); // ElapsedTime == 2f 정확히
        HitPlayer();

        Assert.AreEqual(2f, gm.LastGameResult.SurvivalTime);
        Assert.AreEqual(2f, gm.LastGameResult.BestTime);
        Assert.IsFalse(gm.LastGameResult.IsNewRecord);
    }

    [Test]
    public void HigherThanBest_NewRecord_BestUpdated()
    {
        RecordStore.TryUpdateRecord(1.5f);
        gm.StartGame();
        Frame(2f);
        HitPlayer();

        Assert.IsTrue(gm.LastGameResult.IsNewRecord);
        Assert.AreEqual(2f, gm.LastGameResult.BestTime, 1e-6f);
        Assert.AreEqual(2f, RecordStore.GetBestTime(), 1e-6f);
    }

    [Test]
    public void DuplicateHits_RecordAndResultProcessedOnce()
    {
        gm.StartGame();
        Frame(2f);
        HitPlayer();
        GameResult first = gm.LastGameResult;

        // 기록을 지워 두면, 기록 처리가 다시 일어날 경우 2초가 다시 저장되어 드러난다.
        BestTimeBackup.ClearForTest();
        Missile other = spawner.SpawnMissile();
        gm.HandlePlayerHit(other);
        gm.EndGame();
        Assert.IsFalse(detector.CheckMissileCollision(other), "피격 판정도 다시 일어나지 않음");
        Frame(1f);

        Assert.AreEqual(0f, RecordStore.GetBestTime(), "게임 오버 이후 기록 저장 없음");
        Assert.AreEqual(1, results.Count, "OnGameResultReady 한 번");
        Assert.AreEqual(first.SurvivalTime, gm.LastGameResult.SurvivalTime);
        Assert.AreEqual(first.IsNewRecord, gm.LastGameResult.IsNewRecord);
    }

    [Test]
    public void ResultEvent_AfterGameOverState_SameAsLastGameResult()
    {
        var order = new List<string>();
        gm.OnGameStateChanged += s => order.Add("state:" + s);
        gm.OnGameResultReady += _ => order.Add("result");

        gm.StartGame();
        Frame(3f);
        HitPlayer();

        CollectionAssert.AreEqual(new[] { "state:Playing", "state:GameOver", "result" }, order);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(gm.LastGameResult.SurvivalTime, results[0].SurvivalTime);
        Assert.AreEqual(gm.LastGameResult.BestTime, results[0].BestTime);
        Assert.AreEqual(gm.LastGameResult.IsNewRecord, results[0].IsNewRecord);
        Assert.IsFalse(timer.IsRunning, "결과 시점에 타이머 정지");
        Assert.IsFalse(spawner.IsSpawning);
    }

    [Test]
    public void LeavingDuringPlay_NoRecordNoResult()
    {
        // Exit/Restart 버튼은 씬을 전환할 뿐 EndGame을 호출하지 않는다. 씬 언로드(오브젝트 파괴)를 흉내 낸다.
        gm.StartGame();
        Frame(5f);
        Assert.AreEqual(GameState.Playing, gm.CurrentState);

        Object.DestroyImmediate(managerGo);
        managerGo = null;
        Frame(1f);

        Assert.AreEqual(0f, RecordStore.GetBestTime(), "진행 중 이탈은 기록하지 않음");
        Assert.AreEqual(0, results.Count);
        CollectionAssert.AreEqual(new[] { GameState.Playing }, events, "GameOver가 발생하지 않음");
    }

    [Test]
    public void StartGame_ClearsPreviousResult_KeepsBestTime()
    {
        gm.StartGame();
        Frame(2f);
        HitPlayer();
        Assert.IsTrue(gm.HasGameResult);

        foreach (Missile m in new List<Missile>(spawner.ActiveMissiles)) m.Despawn();
        gm.StartGame();
        Assert.IsFalse(gm.HasGameResult);
        Assert.AreEqual(2f, RecordStore.GetBestTime(), 1e-6f, "재시작해도 최고기록 유지");

        Frame(1f);
        HitPlayer();
        Assert.IsFalse(gm.LastGameResult.IsNewRecord);
        Assert.AreEqual(1f, gm.LastGameResult.SurvivalTime, 1e-6f);
        Assert.AreEqual(2f, gm.LastGameResult.BestTime, 1e-6f);
        Assert.AreEqual(2, results.Count);
    }

    // ---------- Stage 7: 사망 애니메이션 완료 후 결과 전달 ----------

    /// <summary>Player/Visual에 PlayerAnimationController를 붙인다. GameManager는 StartGame에서 자동으로 찾는다.</summary>
    private PlayerAnimationController AddAnimationController()
    {
        var visual = new GameObject("Visual");
        visual.transform.SetParent(playerGo.transform, false);
        return visual.AddComponent<PlayerAnimationController>();
    }

    [Test]
    public void WithoutAnimationController_ResultPublishedImmediately()
    {
        gm.StartGame();
        Frame(1f);
        HitPlayer();
        Assert.AreEqual(1, results.Count);
        Assert.IsTrue(gm.IsGameResultPublished);
        Assert.IsFalse(gm.IsWaitingForDeathAnimation);
    }

    [Test]
    public void WithAnimationController_RecordSavedImmediately_ResultAfterDeathFinished()
    {
        PlayerAnimationController anim = AddAnimationController();
        gm.StartGame();
        Frame(2f);
        HitPlayer();

        // 기록/결과는 게임 오버 즉시 확정
        Assert.AreEqual(GameState.GameOver, gm.CurrentState);
        Assert.IsTrue(gm.HasGameResult);
        Assert.AreEqual(2f, RecordStore.GetBestTime(), 1e-6f, "기록은 사망 연출 전에 저장");
        Assert.IsTrue(gm.LastGameResult.IsNewRecord);
        // 결과 전달은 사망 애니메이션 종료까지 대기
        Assert.AreEqual(0, results.Count);
        Assert.IsTrue(gm.IsWaitingForDeathAnimation);

        anim.Tick(Dt); // Death 진입 → (EditMode: 재생 불가) 즉시 완료 → OnDeathFinished
        Assert.AreEqual(PlayerAnimationController.AnimState.Death, anim.CurrentState);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(gm.LastGameResult.SurvivalTime, results[0].SurvivalTime);
        Assert.IsFalse(gm.IsWaitingForDeathAnimation);

        for (int i = 0; i < 50; i++) anim.Tick(Dt);
        anim.NotifyDeathAnimationFinished();
        gm.TickResultWait(10f);
        Assert.AreEqual(1, results.Count, "결과는 한 번만");
    }

    [Test]
    public void DeathFinishedNeverArrives_TimeoutPublishes()
    {
        AddAnimationController(); // Tick하지 않음 → 완료 신호 없음
        gm.StartGame();
        HitPlayer();
        Assert.AreEqual(0, results.Count);

        gm.TickResultWait(2.9f);
        Assert.AreEqual(0, results.Count);
        gm.TickResultWait(0.2f); // 기본 3초
        Assert.AreEqual(1, results.Count, "무한 대기 없음");
        gm.TickResultWait(5f);
        Assert.AreEqual(1, results.Count);
    }

    [Test]
    public void DisabledAnimationController_ResultPublishedImmediately()
    {
        PlayerAnimationController anim = AddAnimationController();
        anim.enabled = false;
        gm.StartGame();
        HitPlayer();
        Assert.AreEqual(1, results.Count);
    }

    [Test]
    public void AnimationControllerRemovedWhileWaiting_PublishesOnNextTick()
    {
        PlayerAnimationController anim = AddAnimationController();
        gm.StartGame();
        HitPlayer();
        Assert.AreEqual(0, results.Count);

        Object.DestroyImmediate(anim.gameObject);
        gm.TickResultWait(Dt);
        Assert.AreEqual(1, results.Count);
    }

    [Test]
    public void ResultEvent_OrderAndContent_WithDeathAnimation()
    {
        PlayerAnimationController anim = AddAnimationController();
        var order = new List<string>();
        gm.OnGameStateChanged += s => order.Add("state:" + s);
        gm.OnGameResultReady += r =>
        {
            order.Add("result");
            Assert.AreEqual(r.BestTime, RecordStore.GetBestTime(), "결과 전달 시점에 기록은 이미 저장됨");
        };
        anim.OnDeathFinished += () => order.Add("deathFinished");

        gm.StartGame();
        Frame(1.5f);
        HitPlayer();
        anim.Tick(Dt);

        CollectionAssert.AreEqual(new[] { "state:Playing", "state:GameOver", "deathFinished", "result" }, order);
    }

    [Test]
    public void RestartWhileWaitingForDeath_OldDeathDoesNotAffectNewGame()
    {
        PlayerAnimationController anim = AddAnimationController();
        gm.StartGame();
        Frame(2f);
        HitPlayer();
        Assert.IsTrue(gm.IsWaitingForDeathAnimation);

        foreach (Missile m in new List<Missile>(spawner.ActiveMissiles)) m.Despawn();
        gm.StartGame(); // 사망 연출 도중 재시작
        Assert.IsFalse(gm.IsWaitingForDeathAnimation);
        Assert.IsFalse(gm.HasGameResult);

        anim.Tick(Dt);
        Assert.AreEqual("IdleSouth", anim.CurrentStateName, "새 게임은 IdleSouth부터");
        anim.NotifyDeathAnimationFinished();
        gm.TickResultWait(10f);
        Assert.AreEqual(0, results.Count, "이전 사망의 완료 신호/타임아웃이 새 게임에 결과를 보내지 않음");
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.AreEqual(2f, RecordStore.GetBestTime(), 1e-6f, "기록 유지");

        Frame(1f);
        HitPlayer();
        anim.Tick(Dt);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(1f, results[0].SurvivalTime, 1e-6f);
        Assert.IsFalse(results[0].IsNewRecord);
    }

    // ---------- Stage 8: 충돌 이펙트 ----------

    private GameObject CreateEffectTemplate(bool withHitEffect = true)
    {
        var template = new GameObject("HitEffectTemplate");
        template.AddComponent<SpriteRenderer>();
        if (withHitEffect) template.AddComponent<HitEffect>();
        template.transform.SetParent(managerGo.transform); // TearDown에서 함께 정리
        SetRef(gm, "hitEffectPrefab", template);
        return template;
    }

    private static int CountClones(string templateName)
    {
        return Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name == templateName + "(Clone)");
    }

    private Missile SpawnMovedMissile()
    {
        Missile m = spawner.SpawnMissile();
        for (int i = 0; i < 40; i++) m.Tick(Dt); // 생성 위치에서 이동한 뒤 피격
        Assert.AreNotEqual(m.SpawnPosition, m.Position);
        return m;
    }

    [Test]
    public void MissileHit_SpawnsOneEffect_AtMissileWorldPosition_AsRootObject()
    {
        CreateEffectTemplate();
        gm.StartGame();
        Missile m = SpawnMovedMissile();

        Assert.IsTrue(detector.CheckMissileCollision(m));

        GameObject effect = gm.LastHitEffect;
        Assert.IsNotNull(effect);
        Assert.AreEqual(1, CountClones("HitEffectTemplate"));
        Assert.AreEqual(m.Position.x, effect.transform.position.x, 1e-5f, "피격 미사일의 현재 좌표");
        Assert.AreEqual(m.Position.y, effect.transform.position.y, 1e-5f);
        Assert.IsNull(effect.transform.parent, "Container가 없으면 씬 루트(미사일/플레이어 자식 아님)");
        Assert.IsNotNull(effect.GetComponent<HitEffect>());
        Assert.AreEqual(GameState.GameOver, gm.CurrentState);
        Object.DestroyImmediate(effect);
    }

    [Test]
    public void MissileHit_WithEffectContainer_ParentedToContainer()
    {
        CreateEffectTemplate();
        var container = new GameObject("EffectContainer");
        container.transform.position = new Vector3(3f, -2f, 0f);
        SetRef(gm, "effectContainer", container.transform);
        gm.StartGame();
        Missile m = SpawnMovedMissile();

        Assert.IsTrue(detector.CheckMissileCollision(m));

        GameObject effect = gm.LastHitEffect;
        Assert.AreSame(container.transform, effect.transform.parent);
        Assert.AreEqual(m.Position.x, effect.transform.position.x, 1e-5f, "부모가 있어도 월드 좌표 유지");
        Assert.AreEqual(m.Position.y, effect.transform.position.y, 1e-5f);
        Object.DestroyImmediate(container);
    }

    [Test]
    public void JumpDodge_NoEffect()
    {
        CreateEffectTemplate();
        gm.StartGame();
        Missile m = spawner.SpawnMissile();
        Assert.IsTrue(player.TryJump());
        for (int i = 0; i < 5; i++) player.UpdateJump(Dt);
        Assert.GreaterOrEqual(player.JumpHeight, detector.DodgeHeightThreshold);

        Assert.IsFalse(detector.CheckMissileCollision(m));
        Assert.IsNull(gm.LastHitEffect);
        Assert.AreEqual(0, CountClones("HitEffectTemplate"));
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
    }

    [Test]
    public void DuplicateHits_OnlyOneEffect()
    {
        CreateEffectTemplate();
        gm.StartGame();
        Missile first = HitPlayer();
        GameObject effect = gm.LastHitEffect;

        Missile other = spawner.SpawnMissile();
        for (int i = 0; i < 5; i++)
        {
            detector.CheckMissileCollision(other); // OnTriggerStay2D 반복과 같은 경로
            detector.EvaluateCollider(other.GetComponent<Collider2D>());
            gm.HandlePlayerHit(other);
            gm.EndGame();
        }

        Assert.AreEqual(1, CountClones("HitEffectTemplate"));
        Assert.AreSame(effect, gm.LastHitEffect);
        Assert.AreSame(first, gm.LastHitMissile);
        Object.DestroyImmediate(effect);
    }

    [Test]
    public void NoEffectPrefab_GameOverAndRecordNormal()
    {
        gm.StartGame();
        Frame(2f);
        HitPlayer();
        Assert.AreEqual(GameState.GameOver, gm.CurrentState);
        Assert.IsNull(gm.LastHitEffect);
        Assert.AreEqual(2f, RecordStore.GetBestTime(), 1e-6f);
        Assert.AreEqual(1, results.Count);
    }

    [Test]
    public void EffectPrefabWithoutHitEffect_ComponentAddedForAutoRemoval()
    {
        CreateEffectTemplate(withHitEffect: false);
        gm.StartGame();
        HitPlayer();
        var hitEffect = gm.LastHitEffect.GetComponent<HitEffect>();
        Assert.IsNotNull(hitEffect, "자동 제거 컴포넌트 추가");
        hitEffect.Tick(10f);
        Assert.IsTrue(gm.LastHitEffect == null, "보호 시간 후 제거");
    }

    [Test]
    public void EffectRemoval_DoesNotAffectGameObjects_AndRunsAfterGameOver()
    {
        CreateEffectTemplate();
        gm.StartGame();
        Frame(1f);
        Missile m = HitPlayer();
        GameObject effect = gm.LastHitEffect;
        var hitEffect = effect.GetComponent<HitEffect>();

        // 게임 오버 처리(미사일 정지, 사망)가 끝나도 이펙트는 남아 계속 진행
        Assert.IsTrue(effect != null);
        Assert.IsFalse(hitEffect.IsFinished);
        hitEffect.Tick(1f);
        Assert.IsTrue(effect != null);
        Assert.AreEqual(1f, hitEffect.Elapsed, 1e-6f);

        hitEffect.Tick(5f);
        Assert.IsTrue(effect == null);
        Assert.IsTrue(m != null && !m.IsDespawned, "미사일 유지");
        Assert.IsTrue(player != null && player.IsDead);
        Assert.IsTrue(gm != null);
        Assert.AreEqual(GameState.GameOver, gm.CurrentState);
        Assert.AreEqual(1f, RecordStore.GetBestTime(), 1e-6f);
    }

    [Test]
    public void Effect_IndependentOfDeathAnimation_ResultStillAfterDeath()
    {
        CreateEffectTemplate();
        PlayerAnimationController anim = AddAnimationController();
        gm.StartGame();
        HitPlayer();
        GameObject effect = gm.LastHitEffect;
        Assert.IsNotNull(effect);
        Assert.AreEqual(0, results.Count, "결과는 Death 완료 대기(이펙트와 무관)");

        anim.Tick(Dt); // Death 완료
        Assert.AreEqual(1, results.Count);
        Assert.IsTrue(effect != null, "이펙트는 Death와 별개로 자기 수명대로 재생");
        Object.DestroyImmediate(effect);
    }

    [Test]
    public void Restart_ClearsLastHitEffect_NextGameOverSpawnsNewOne()
    {
        CreateEffectTemplate();
        gm.StartGame();
        HitPlayer();
        GameObject first = gm.LastHitEffect;

        foreach (Missile mm in new List<Missile>(spawner.ActiveMissiles)) mm.Despawn();
        gm.StartGame();
        Assert.IsNull(gm.LastHitEffect);
        HitPlayer();
        Assert.IsNotNull(gm.LastHitEffect);
        Assert.AreNotSame(first, gm.LastHitEffect);
        Object.DestroyImmediate(first);
        Object.DestroyImmediate(gm.LastHitEffect);
    }
}
