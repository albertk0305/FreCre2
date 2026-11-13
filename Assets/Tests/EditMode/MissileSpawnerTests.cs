using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class MissileSpawnerTests
{
    private const float Eps = 1e-4f;
    private static readonly Vector2 Center = new Vector2(0.5f, -0.5f);

    private GameObject arenaGo;
    private GameObject cameraGo;
    private GameObject templateGo;
    private GameObject spawnerGo;
    private MissileSpawner spawner;
    private Random.State savedRandom;

    [SetUp]
    public void SetUp()
    {
        savedRandom = Random.state;
        Random.InitState(12345);

        arenaGo = new GameObject("Arena");
        var arena = arenaGo.AddComponent<ArenaBounds>();
        arena.Center = Center;
        arena.HalfSize = 2.0f;

        cameraGo = new GameObject("Cam");
        var cam = cameraGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 2.5f;
        cam.aspect = 16f / 9f;
        // 카메라를 일부러 경기장 중심과 다른 곳에 둬서 생성 기준이 카메라가 아님을 검증
        cameraGo.transform.position = new Vector3(3f, 2f, -10f);

        templateGo = new GameObject("MissileTemplate");
        templateGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        templateGo.AddComponent<CircleCollider2D>().isTrigger = true;
        var template = templateGo.AddComponent<Missile>();

        spawnerGo = new GameObject("MissileSpawner");
        spawner = spawnerGo.AddComponent<MissileSpawner>();
        var so = new SerializedObject(spawner);
        so.FindProperty("missilePrefab").objectReferenceValue = template;
        so.FindProperty("arenaBounds").objectReferenceValue = arena;
        so.FindProperty("targetCamera").objectReferenceValue = cam;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(spawnerGo); // 자식으로 생성된 미사일도 함께 파괴
        Object.DestroyImmediate(templateGo);
        Object.DestroyImmediate(cameraGo);
        Object.DestroyImmediate(arenaGo);
        Random.state = savedRandom;
    }

    // ---------- 기본값 ----------

    [Test]
    public void InspectorDefaults_MatchStage3Spec()
    {
        Assert.AreEqual(5.0f, spawner.SpawnRadius, 1e-6f);
        Assert.AreEqual(1.15f, spawner.AimTargetHalfSize, 1e-6f);
        Assert.AreEqual(1.67f, spawner.MinMissileSpeed, 1e-6f);
        Assert.AreEqual(2.33f, spawner.MaxMissileSpeed, 1e-6f);
        Assert.AreEqual(1.80f, spawner.SpawnInterval, 1e-6f);
        Assert.AreEqual(5, spawner.MaxActiveMissiles);
        Assert.AreEqual(0.33f, spawner.DespawnMargin, 1e-6f);
    }

    // ---------- 생성 위치 ----------

    [TestCase(0f, 5.5f, -0.5f)]
    [TestCase(90f, 0.5f, 4.5f)]
    [TestCase(180f, -4.5f, -0.5f)]
    [TestCase(270f, 0.5f, -5.5f)]
    public void SpawnPositionFromAngle_UsesCenterAndRadius(float angle, float ex, float ey)
    {
        Vector2 p = MissileSpawner.SpawnPositionFromAngle(Center, 5f, angle);
        Assert.AreEqual(ex, p.x, Eps);
        Assert.AreEqual(ey, p.y, Eps);
    }

    [Test]
    public void CalculateSpawnPosition_OnCircleAroundArenaCenter_AllQuadrants()
    {
        var quadrants = new bool[4];
        var octants = new int[8];
        for (int i = 0; i < 4000; i++)
        {
            Vector2 p = spawner.CalculateSpawnPosition();
            Vector2 d = p - Center;
            Assert.AreEqual(5f, d.magnitude, Eps, "ArenaBounds.Center 기준 반지름 5");

            float a = (Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 360f) % 360f;
            quadrants[(int)(a / 90f) % 4] = true;
            octants[(int)(a / 45f) % 8]++;
        }
        CollectionAssert.AreEqual(new[] { true, true, true, true }, quadrants);
        foreach (int c in octants)
        {
            Assert.That(c, Is.InRange(400, 600), "45° 구간별 분포가 대략 균등 (기대 500)");
        }
    }

    // ---------- 방향 ----------

    [Test]
    public void CalculateDirection_NormalizedAndAimsInsideTargetArea()
    {
        for (int i = 0; i < 1000; i++)
        {
            Vector2 spawn = spawner.CalculateSpawnPosition();
            Vector2 dir = spawner.CalculateDirection(spawn, out Vector2 target);

            Assert.AreEqual(1f, dir.magnitude, 1e-4f);
            Assert.That(target.x - Center.x, Is.InRange(-1.15f - 1e-5f, 1.15f + 1e-5f));
            Assert.That(target.y - Center.y, Is.InRange(-1.15f - 1e-5f, 1.15f + 1e-5f));

            Vector2 expected = (target - spawn).normalized;
            Assert.AreEqual(expected.x, dir.x, 1e-5f);
            Assert.AreEqual(expected.y, dir.y, 1e-5f);
        }
    }

    [Test]
    public void CalculateDirection_IsNotQuantizedToEightDirections()
    {
        int offAxis = 0;
        for (int i = 0; i < 200; i++)
        {
            Vector2 dir = spawner.CalculateDirection(spawner.CalculateSpawnPosition());
            float a = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float rem = Mathf.Abs(Mathf.DeltaAngle(a, Mathf.Round(a / 45f) * 45f));
            if (rem > 1f) offAxis++;
        }
        Assert.Greater(offAxis, 150);
    }

    [Test]
    public void CalculateSpeed_WithinRange_AndVaries()
    {
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < 1000; i++)
        {
            float s = spawner.CalculateSpeed();
            Assert.That(s, Is.InRange(1.67f, 2.33f));
            min = Mathf.Min(min, s);
            max = Mathf.Max(max, s);
        }
        Assert.Greater(max - min, 0.5f);
    }

    // ---------- 제거 범위 ----------

    [Test]
    public void CameraBounds_SceneValues()
    {
        Bounds b = MissileSpawner.CalculateCameraBounds(Center, 2.5f, 16f / 9f, 0.33f);
        Assert.AreEqual(0.5f, b.center.x, Eps);
        Assert.AreEqual(-0.5f, b.center.y, Eps);
        Assert.AreEqual(2.5f * 16f / 9f + 0.33f, b.extents.x, Eps);
        Assert.AreEqual(2.83f, b.extents.y, Eps);
    }

    [Test]
    public void CalculateDespawnBounds_UsesCamera()
    {
        Bounds b = spawner.CalculateDespawnBounds();
        Assert.AreEqual(3f, b.center.x, Eps);
        Assert.AreEqual(2f, b.center.y, Eps);
        Assert.AreEqual(2.83f, b.extents.y, Eps);
    }

    // ---------- 타이머 / 상한 ----------

    [Test]
    public void FirstMissile_SpawnsAt1Point8Seconds()
    {
        spawner.StartSpawning();
        for (int i = 0; i < 89; i++) spawner.Tick(0.02f); // ≈1.78초
        Assert.AreEqual(0, spawner.ActiveMissileCount);
        spawner.Tick(0.03f); // ≈1.81초
        Assert.AreEqual(1, spawner.ActiveMissileCount);

        // 정확히 1.80초 한 번에
        spawner.ActiveMissiles[0].Despawn();
        spawner.StartSpawning();
        spawner.Tick(1.80f);
        Assert.AreEqual(1, spawner.ActiveMissileCount);
    }

    [Test]
    public void SpawnedMissile_IsInitializedFromSpawner()
    {
        Missile m = spawner.SpawnMissile();
        Assert.IsNotNull(m);
        Assert.IsTrue(m.IsMoving);
        Assert.AreEqual(5f, (m.SpawnPosition - Center).magnitude, Eps);
        Assert.AreEqual(m.SpawnPosition, (Vector2)m.transform.position);
        Assert.That(m.MoveSpeed, Is.InRange(1.67f, 2.33f));
        Assert.AreEqual(1f, m.MoveDirection.magnitude, 1e-4f);
        Assert.IsFalse(m.HasPassedTarget, "생성 직후에는 목표점을 지나지 않음");
    }

    [Test]
    public void MaxActive_SkipsAttempt_AndDoesNotBurstWhenSlotFrees()
    {
        spawner.StartSpawning();
        // 20 간격 동안 진행 → 상한 5에서 멈춤 (EditMode에서는 미사일이 움직이지 않아 제거되지 않음)
        for (int i = 0; i < 20; i++) spawner.Tick(1.80f);
        Assert.AreEqual(5, spawner.ActiveMissileCount);

        spawner.ActiveMissiles[0].Despawn();
        spawner.ActiveMissiles[0].Despawn();
        Assert.AreEqual(3, spawner.ActiveMissileCount);

        spawner.Tick(1.80f);
        Assert.AreEqual(4, spawner.ActiveMissileCount, "빈자리가 2개여도 한 번에 1개만");
        spawner.Tick(1.80f);
        Assert.AreEqual(5, spawner.ActiveMissileCount);
    }

    [Test]
    public void LargeFrameDelta_SpawnsAtMostOnePerTick()
    {
        spawner.StartSpawning();
        spawner.Tick(10f);
        Assert.AreEqual(1, spawner.ActiveMissileCount);
    }

    [Test]
    public void SpawnMissile_RespectsCap()
    {
        for (int i = 0; i < 5; i++) Assert.IsNotNull(spawner.SpawnMissile());
        Assert.IsNull(spawner.SpawnMissile());
        Assert.AreEqual(5, spawner.ActiveMissileCount);
    }

    [Test]
    public void StopSpawning_PreventsNewMissiles()
    {
        spawner.StartSpawning();
        spawner.StopSpawning();
        for (int i = 0; i < 10; i++) spawner.Tick(1.80f);
        Assert.AreEqual(0, spawner.ActiveMissileCount);
        Assert.IsFalse(spawner.IsSpawning);
    }

    [Test]
    public void NotSpawning_BeforeStart()
    {
        spawner.Tick(5f);
        Assert.AreEqual(0, spawner.ActiveMissileCount);
    }

    // ---------- 목록 관리 ----------

    [Test]
    public void DespawnEvent_RemovesFromList()
    {
        Missile a = spawner.SpawnMissile();
        Missile b = spawner.SpawnMissile();
        Assert.AreEqual(2, spawner.ActiveMissileCount);

        a.Despawn();
        Assert.AreEqual(1, spawner.ActiveMissileCount);
        Assert.AreSame(b, spawner.ActiveMissiles[0]);
    }

    [Test]
    public void ExternallyDestroyedMissile_IsPrunedFromList()
    {
        Missile a = spawner.SpawnMissile();
        spawner.SpawnMissile();
        Object.DestroyImmediate(a.gameObject);

        Assert.AreEqual(1, spawner.ActiveMissileCount);
        foreach (var m in spawner.ActiveMissiles) Assert.IsTrue(m != null);
    }

    [Test]
    public void MissileMovingThroughArena_DespawnsAndLeavesList()
    {
        // 카메라를 실제 씬 위치로
        cameraGo.transform.position = new Vector3(0.5f, -0.5f, -10f);
        Missile m = spawner.SpawnMissile();

        int steps = 0;
        while (spawner.ActiveMissileCount > 0 && steps < 2000)
        {
            m.Tick(0.02f);
            steps++;
        }
        Assert.AreEqual(0, spawner.ActiveMissileCount);
        Assert.Less(steps * 0.02f, 10f, "최대 수명 전에 정상 경로로 제거되어야 함");
    }

    [Test]
    public void StopAllMissiles_StopsEveryActiveMissile()
    {
        for (int i = 0; i < 3; i++) spawner.SpawnMissile();
        spawner.StopAllMissiles();
        foreach (var m in spawner.ActiveMissiles)
        {
            Vector2 p = m.Position;
            m.Tick(0.5f);
            Assert.IsFalse(m.IsMoving);
            Assert.AreEqual(p, m.Position);
        }
        Assert.AreEqual(3, spawner.ActiveMissileCount);
    }
}
