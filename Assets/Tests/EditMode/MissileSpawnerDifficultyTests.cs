using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stage 5: SurvivalTimer.ElapsedTime → DifficultyManager.GetCurrentDifficulty(time) → DifficultySnapshot → MissileSpawner.
/// EditMode에서는 Update/FixedUpdate가 돌지 않으므로 timer.Tick / spawner.Tick을 직접 호출한다(미사일은 움직이지 않음).
/// </summary>
public class MissileSpawnerDifficultyTests
{
    private const float Eps = 1e-4f;
    private static readonly Vector2 Center = new Vector2(0.5f, -0.5f);

    private GameObject arenaGo, cameraGo, templateGo, spawnerGo, systemsGo;
    private MissileSpawner spawner;
    private SurvivalTimer timer;
    private DifficultyManager dm;
    private Random.State savedRandom;

    [SetUp]
    public void SetUp()
    {
        savedRandom = Random.state;
        Random.InitState(5555);

        arenaGo = new GameObject("Arena");
        var arena = arenaGo.AddComponent<ArenaBounds>();
        arena.Center = Center;
        arena.HalfSize = 2f;

        cameraGo = new GameObject("Cam");
        var cam = cameraGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 2.5f;
        cam.aspect = 16f / 9f;
        cameraGo.transform.position = new Vector3(Center.x, Center.y, -10f);

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
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(spawnerGo);
        Object.DestroyImmediate(systemsGo);
        Object.DestroyImmediate(templateGo);
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

    private void SetDm(string field, float value)
    {
        var so = new SerializedObject(dm);
        so.FindProperty(field).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private void SetDm(string field, int value)
    {
        var so = new SerializedObject(dm);
        so.FindProperty(field).intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>GameManager.StartGame()과 같은 순서: 타이머 초기화 → 시작 → 생성 시작.</summary>
    private void StartAt(float elapsed)
    {
        timer.ResetTimer();
        timer.StartTimer();
        timer.Tick(elapsed);
        spawner.StartSpawning();
    }

    /// <summary>한 프레임: SurvivalTimer(실행 순서 -50) → MissileSpawner.</summary>
    private void Frame(float dt)
    {
        timer.Tick(dt);
        spawner.Tick(dt);
    }

    // ---------- 첫 생성 / 간격 ----------

    [Test]
    public void FirstMissile_SpawnsAtInitialInterval_1Point8s()
    {
        StartAt(0f);
        Assert.AreEqual(1.80f, spawner.SpawnInterval, 1e-6f);

        for (int i = 0; i < 89; i++) Frame(0.02f); // ≈1.78초
        Assert.AreEqual(0, spawner.ActiveMissileCount);
        Frame(0.03f); // ≈1.81초
        Assert.AreEqual(1, spawner.ActiveMissileCount);
    }

    [Test]
    public void EachAttempt_FetchesLatestSnapshot_AndAppliesItsInterval()
    {
        StartAt(0f);
        timer.Tick(60f); // 생존 60초 경과 (스포너 시도 전)
        spawner.Tick(1.80f); // 시작 시점 간격 1.80초 대기 후 시도 → 60초 난이도 수신
        Assert.AreEqual(1, spawner.ActiveMissileCount);
        Assert.AreEqual(1.05f, spawner.SpawnInterval, Eps, "다음 간격 = 60초 스냅샷의 1.05초");
        Assert.AreEqual(9, spawner.MaxActiveMissiles);

        spawner.Tick(1.03f);
        Assert.AreEqual(1, spawner.ActiveMissileCount);
        spawner.Tick(0.03f);
        Assert.AreEqual(2, spawner.ActiveMissileCount);
    }

    [Test]
    public void SpawnInterval_NeverBelowMinimum_LongRun()
    {
        StartAt(0f);
        const float dt = 0.02f;
        var spawnTimes = new List<float>();
        for (int i = 0; i < 15000; i++) // 300초
        {
            Frame(dt);
            if (spawner.ActiveMissileCount > 0)
            {
                spawnTimes.Add(timer.ElapsedTime);
                spawner.ActiveMissiles[0].Despawn(); // 상한 영향 제거
            }
        }

        Assert.AreEqual(1.80f, spawnTimes[0], 0.03f);
        for (int i = 1; i < spawnTimes.Count; i++)
        {
            float gap = spawnTimes[i] - spawnTimes[i - 1];
            Assert.GreaterOrEqual(gap, 0.45f - dt - 1e-3f, $"#{i} gap {gap}");
        }
        Assert.AreEqual(0.45f, spawner.SpawnInterval, Eps);
        // 120초 이후 구간: 0.45초 간격 → 180초 동안 약 400회
        int late = spawnTimes.FindAll(t => t >= 120f).Count;
        Assert.That(late, Is.InRange(380, 401));
    }

    [Test]
    public void StartSpawning_AfterLongTime_UsesMinimumInterval()
    {
        StartAt(1000f);
        Assert.AreEqual(0.45f, spawner.SpawnInterval, Eps);
        spawner.Tick(0.44f);
        Assert.AreEqual(0, spawner.ActiveMissileCount);
        spawner.Tick(0.02f);
        Assert.AreEqual(1, spawner.ActiveMissileCount);
    }

    // ---------- 동시 개수 상한 ----------

    [Test]
    public void ActiveCount_NeverExceedsSnapshotCap_LongRun()
    {
        StartAt(0f);
        for (int i = 0; i < 10000; i++) // 200초, EditMode라 미사일은 제거되지 않음
        {
            Frame(0.02f);
            int cap = dm.GetCurrentDifficulty(timer.ElapsedTime).MaxActiveMissiles;
            Assert.LessOrEqual(spawner.ActiveMissileCount, cap, $"t={timer.ElapsedTime}");
        }
        Assert.AreEqual(14, spawner.ActiveMissileCount);

        foreach (Missile m in spawner.ActiveMissiles)
        {
            Assert.AreEqual(5f, (m.SpawnPosition - Center).magnitude, Eps, "원형 생성 규칙 유지");
        }
    }

    [Test]
    public void Cap_At135s_Is14()
    {
        StartAt(135f);
        for (int i = 0; i < 30; i++) spawner.Tick(1.80f);
        Assert.AreEqual(14, spawner.ActiveMissileCount);
        Assert.AreEqual(14, spawner.MaxActiveMissiles);
    }

    [Test]
    public void Cap_IncreasesWhenSurvivalTimeCrossesStep()
    {
        StartAt(0f);
        for (int i = 0; i < 10; i++) spawner.Tick(1.80f);
        Assert.AreEqual(5, spawner.ActiveMissileCount);

        timer.Tick(15f); // 15초 → 상한 6
        spawner.Tick(spawner.SpawnInterval);
        Assert.AreEqual(6, spawner.ActiveMissileCount);
        spawner.Tick(spawner.SpawnInterval);
        Assert.AreEqual(6, spawner.ActiveMissileCount);
    }

    [Test]
    public void AtCap_SkipsAttempt_AndResetsTimer()
    {
        StartAt(0f);
        for (int i = 0; i < 5; i++) spawner.Tick(1.80f);
        Assert.AreEqual(5, spawner.ActiveMissileCount);

        spawner.Tick(2.5f); // 시도 → 상한 → 생략
        Assert.AreEqual(5, spawner.ActiveMissileCount);
        Assert.AreEqual(0f, spawner.SpawnTimer, "생략 시 남은 시간(0.7초)을 이월하지 않고 0으로 초기화");
        Assert.IsTrue(spawner.IsSpawning);

        // 빈자리가 생겨도 다음 시도까지 전체 간격을 다시 기다린다
        spawner.ActiveMissiles[0].Despawn();
        spawner.Tick(1.70f);
        Assert.AreEqual(4, spawner.ActiveMissileCount);
        spawner.Tick(0.11f);
        Assert.AreEqual(5, spawner.ActiveMissileCount);
    }

    [Test]
    public void SlotsFreed_NoBurstSpawn()
    {
        StartAt(135f); // 간격 0.45, 상한 14
        for (int i = 0; i < 20; i++) spawner.Tick(0.45f);
        Assert.AreEqual(14, spawner.ActiveMissileCount);

        for (int i = 0; i < 6; i++) spawner.ActiveMissiles[0].Despawn();
        Assert.AreEqual(8, spawner.ActiveMissileCount);

        spawner.Tick(10f); // 큰 프레임 지연 + 빈자리 6개
        Assert.AreEqual(9, spawner.ActiveMissileCount, "한 번에 1개만");
        spawner.Tick(0.44f);
        Assert.AreEqual(9, spawner.ActiveMissileCount);
        spawner.Tick(0.02f);
        Assert.AreEqual(10, spawner.ActiveMissileCount);
    }

    // ---------- 속도 ----------

    [Test]
    public void Speed_WithinSnapshotRange_AtAllTimes()
    {
        SetDm("initialMaxMissiles", 200);
        SetDm("maximumMissiles", 200);
        StartAt(0f);
        foreach (float t in new[] { 0f, 30f, 60f, 120f, 135f, 600f })
        {
            timer.ResetTimer();
            timer.Tick(t);
            for (int i = 0; i < 30; i++)
            {
                Missile m = spawner.SpawnMissile();
                Assert.IsNotNull(m);
                Assert.That(m.MoveSpeed, Is.InRange(1.67f, 2.33f), $"t={t}");
            }
        }
    }

    [Test]
    public void Speed_UsesDifficultyManagerRange()
    {
        SetDm("minMissileSpeed", 3f);
        SetDm("maxMissileSpeed", 4f);
        SetDm("initialMaxMissiles", 200);
        SetDm("maximumMissiles", 200);
        StartAt(0f);

        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < 200; i++)
        {
            float s = spawner.SpawnMissile().MoveSpeed;
            Assert.That(s, Is.InRange(3f, 4f));
            min = Mathf.Min(min, s);
            max = Mathf.Max(max, s);
        }
        Assert.Greater(max - min, 0.6f, "범위 안에서 무작위");
        Assert.AreEqual(3f, spawner.MinMissileSpeed, 1e-6f);
        Assert.AreEqual(4f, spawner.MaxMissileSpeed, 1e-6f);
    }

    [Test]
    public void SpawnedMissile_SpeedAndDirection_NeverChangeAfterSpawn()
    {
        StartAt(0f);
        spawner.Tick(1.80f);
        Missile first = spawner.ActiveMissiles[0];
        float speed = first.MoveSpeed;
        Vector2 dir = first.MoveDirection;

        // 시간 경과 + 난이도 설정 변경 + 추가 생성 후에도 기존 미사일 값은 그대로
        timer.Tick(200f);
        SetDm("minMissileSpeed", 5f);
        SetDm("maxMissileSpeed", 6f);
        for (int i = 0; i < 10; i++) spawner.Tick(spawner.SpawnInterval);
        for (int i = 0; i < 10; i++) first.Tick(0.02f);

        Assert.AreEqual(speed, first.MoveSpeed);
        Assert.AreEqual(dir, first.MoveDirection);
        Assert.That(spawner.ActiveMissiles[spawner.ActiveMissileCount - 1].MoveSpeed, Is.InRange(5f, 6f));
    }

    // ---------- 정지 ----------

    [Test]
    public void StopSpawning_And_StopAllMissiles_WithDifficulty()
    {
        StartAt(135f);
        for (int i = 0; i < 5; i++) spawner.Tick(0.45f);
        Assert.AreEqual(5, spawner.ActiveMissileCount);

        spawner.StopSpawning();
        spawner.StopAllMissiles();
        for (int i = 0; i < 20; i++) Frame(0.45f);

        Assert.AreEqual(5, spawner.ActiveMissileCount);
        foreach (Missile m in spawner.ActiveMissiles)
        {
            Vector2 p = m.Position;
            m.Tick(0.5f);
            Assert.IsFalse(m.IsMoving);
            Assert.AreEqual(p, m.Position);
        }
    }
}
