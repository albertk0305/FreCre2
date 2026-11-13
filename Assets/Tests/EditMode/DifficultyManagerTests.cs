using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class DifficultyManagerTests
{
    // 실제 float 계산값 비교 허용 오차 (1.80f - t × 0.0125f 의 단정도 오차는 1e-6 수준)
    private const float FloatEps = 1e-5f;

    private GameObject go;
    private DifficultyManager dm;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("DifficultyManager");
        dm = go.AddComponent<DifficultyManager>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(go);
    }

    private void Set(string field, float value)
    {
        var so = new SerializedObject(dm);
        so.FindProperty(field).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private void Set(string field, int value)
    {
        var so = new SerializedObject(dm);
        so.FindProperty(field).intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------- Inspector 초기값 ----------

    [Test]
    public void InspectorDefaults_MatchBalanceSpec()
    {
        Assert.AreEqual(1.80f, dm.InitialSpawnInterval, 1e-6f);
        Assert.AreEqual(0.45f, dm.MinimumSpawnInterval, 1e-6f);
        Assert.AreEqual(0.0125f, dm.SpawnIntervalDecreaseRate, 1e-7f);
        Assert.AreEqual(5, dm.InitialMaxMissiles);
        Assert.AreEqual(14, dm.MaximumMissiles);
        Assert.AreEqual(15f, dm.MissileCountIncreaseInterval, 1e-6f);
        Assert.AreEqual(1.67f, dm.MinMissileSpeed, 1e-6f);
        Assert.AreEqual(2.33f, dm.MaxMissileSpeed, 1e-6f);
    }

    [Test]
    public void SerializedFieldNames_Exist()
    {
        var so = new SerializedObject(dm);
        foreach (string f in new[]
                 {
                     "survivalTimer", "initialSpawnInterval", "minimumSpawnInterval", "spawnIntervalDecreaseRate",
                     "initialMaxMissiles", "maximumMissiles", "missileCountIncreaseInterval",
                     "minMissileSpeed", "maxMissileSpeed",
                 })
        {
            Assert.IsNotNull(so.FindProperty(f), f);
        }
    }

    // ---------- 시간별 난이도 (실제 float 계산값) ----------

    [TestCase(0f, 1.80f, 5)]
    [TestCase(30f, 1.425f, 7)]
    [TestCase(60f, 1.05f, 9)]
    [TestCase(120f, 0.45f, 13)]
    [TestCase(135f, 0.45f, 14)]
    public void GetCurrentDifficulty_ActualFloatValues(float t, float expectedInterval, int expectedMax)
    {
        DifficultySnapshot s = dm.GetCurrentDifficulty(t);
        Assert.AreEqual(expectedInterval, s.SpawnInterval, FloatEps);
        Assert.AreEqual(expectedMax, s.MaxActiveMissiles);
        Assert.AreEqual(1.67f, s.MinMissileSpeed, 1e-6f);
        Assert.AreEqual(2.33f, s.MaxMissileSpeed, 1e-6f);
    }

    // ---------- 시간별 난이도 (밸런스 문서 4.2 표시값, 소수 둘째 자리 반올림) ----------

    [TestCase(0f, 1.80f)]
    [TestCase(15f, 1.61f)]   // 실제 1.6125
    [TestCase(30f, 1.43f)]   // 실제 1.425
    [TestCase(45f, 1.24f)]   // 실제 1.2375
    [TestCase(60f, 1.05f)]
    [TestCase(75f, 0.86f)]   // 실제 0.8625
    [TestCase(90f, 0.68f)]   // 실제 0.675
    [TestCase(105f, 0.49f)]  // 실제 0.4875
    [TestCase(120f, 0.45f)]
    [TestCase(135f, 0.45f)]
    public void GetCurrentDifficulty_MatchesDisplayedTableWithinRounding(float t, float displayed)
    {
        // 표시값은 반올림된 값이므로 실제 값과의 차이가 반올림 폭(0.005) 이내인지만 확인한다.
        float actual = dm.GetCurrentDifficulty(t).SpawnInterval;
        Assert.That(Mathf.Abs(actual - displayed), Is.LessThanOrEqualTo(0.005f + FloatEps),
            $"t={t}: 실제 {actual} / 표시 {displayed}");
    }

    [Test]
    public void DisplayedValue_IsNotTheActualValue_At30s()
    {
        // 표 표시값 1.43은 실제 계산값이 아니다. 실제 생성 간격은 1.425초.
        float actual = dm.GetCurrentDifficulty(30f).SpawnInterval;
        Assert.AreNotEqual(1.43f, actual);
        Assert.Greater(Mathf.Abs(1.43f - actual), 1e-3f);
        Assert.AreEqual(1.425f, actual, FloatEps);
    }

    [TestCase(0f, 5)]
    [TestCase(14.99f, 5)]
    [TestCase(15f, 6)]
    [TestCase(29.99f, 6)]
    [TestCase(30f, 7)]
    [TestCase(119.99f, 12)]
    [TestCase(120f, 13)]
    [TestCase(134.99f, 13)]
    [TestCase(135f, 14)]
    [TestCase(1000f, 14)]
    public void MaxActiveMissiles_StepsEvery15Seconds_AndCapsAt14(float t, int expected)
    {
        Assert.AreEqual(expected, dm.GetCurrentDifficulty(t).MaxActiveMissiles);
    }

    // ---------- 하한/상한 ----------

    [Test]
    public void SpawnInterval_NeverBelowMinimum()
    {
        Assert.AreEqual(0.45f, dm.GetCurrentDifficulty(108f).SpawnInterval, FloatEps, "108초에 최소값 도달");
        for (float t = 0f; t <= 3600f; t += 0.5f)
        {
            float interval = dm.GetCurrentDifficulty(t).SpawnInterval;
            Assert.GreaterOrEqual(interval, 0.45f - FloatEps, $"t={t}");
            Assert.LessOrEqual(interval, 1.80f + FloatEps, $"t={t}");
        }
    }

    [Test]
    public void SpawnInterval_MonotonicallyNonIncreasing_MaxMissilesNonDecreasing()
    {
        DifficultySnapshot prev = dm.GetCurrentDifficulty(0f);
        for (float t = 0.25f; t <= 300f; t += 0.25f)
        {
            DifficultySnapshot s = dm.GetCurrentDifficulty(t);
            Assert.LessOrEqual(s.SpawnInterval, prev.SpawnInterval + 1e-6f);
            Assert.GreaterOrEqual(s.MaxActiveMissiles, prev.MaxActiveMissiles);
            Assert.LessOrEqual(s.MaxActiveMissiles, 14);
            prev = s;
        }
    }

    [Test]
    public void SpeedRange_FixedRegardlessOfTime()
    {
        foreach (float t in new[] { 0f, 30f, 60f, 120f, 135f, 600f, 1e6f })
        {
            DifficultySnapshot s = dm.GetCurrentDifficulty(t);
            Assert.AreEqual(1.67f, s.MinMissileSpeed, 1e-6f, $"t={t}");
            Assert.AreEqual(2.33f, s.MaxMissileSpeed, 1e-6f, $"t={t}");
        }
    }

    [TestCase(-5f)]
    [TestCase(float.NaN)]
    public void InvalidElapsed_TreatedAsZero(float t)
    {
        DifficultySnapshot s = dm.GetCurrentDifficulty(t);
        Assert.AreEqual(1.80f, s.SpawnInterval, FloatEps);
        Assert.AreEqual(5, s.MaxActiveMissiles);
    }

    [Test]
    public void HugeElapsed_NoOverflow()
    {
        DifficultySnapshot s = dm.GetCurrentDifficulty(float.MaxValue);
        Assert.AreEqual(14, s.MaxActiveMissiles);
        Assert.AreEqual(0.45f, s.SpawnInterval, FloatEps);
        Assert.AreEqual(14, dm.GetCurrentDifficulty(float.PositiveInfinity).MaxActiveMissiles);
    }

    // ---------- Inspector 값 변경 반영 ----------

    [Test]
    public void InspectorValues_AreUsedByCalculation()
    {
        Set("initialSpawnInterval", 2.0f);
        Set("minimumSpawnInterval", 0.5f);
        Set("spawnIntervalDecreaseRate", 0.1f);
        Set("initialMaxMissiles", 3);
        Set("maximumMissiles", 4);
        Set("missileCountIncreaseInterval", 10f);
        Set("minMissileSpeed", 3f);
        Set("maxMissileSpeed", 4f);

        DifficultySnapshot s = dm.GetCurrentDifficulty(10f);
        Assert.AreEqual(1.0f, s.SpawnInterval, FloatEps);
        Assert.AreEqual(4, s.MaxActiveMissiles);
        Assert.AreEqual(3f, s.MinMissileSpeed, 1e-6f);
        Assert.AreEqual(4f, s.MaxMissileSpeed, 1e-6f);

        Assert.AreEqual(0.5f, dm.GetCurrentDifficulty(100f).SpawnInterval, FloatEps);
        Assert.AreEqual(4, dm.GetCurrentDifficulty(100f).MaxActiveMissiles);
    }

    // ---------- SurvivalTimer 연동 ----------

    [Test]
    public void GetCurrentDifficulty_NoArg_UsesSurvivalTimerElapsed()
    {
        Assert.AreEqual(1.80f, dm.GetCurrentDifficulty().SpawnInterval, FloatEps, "타이머 없으면 0초 기준");

        var timer = go.AddComponent<SurvivalTimer>();
        var so = new SerializedObject(dm);
        so.FindProperty("survivalTimer").objectReferenceValue = timer;
        so.ApplyModifiedPropertiesWithoutUndo();

        timer.StartTimer();
        timer.Tick(60f);
        DifficultySnapshot s = dm.GetCurrentDifficulty();
        Assert.AreEqual(1.05f, s.SpawnInterval, FloatEps);
        Assert.AreEqual(9, s.MaxActiveMissiles);
    }

    [Test]
    public void DefaultSnapshot_IsZeroSecondDifficulty()
    {
        DifficultySnapshot d = DifficultyManager.DefaultSnapshot;
        DifficultySnapshot zero = dm.GetCurrentDifficulty(0f);
        Assert.AreEqual(zero.SpawnInterval, d.SpawnInterval, 1e-6f);
        Assert.AreEqual(zero.MaxActiveMissiles, d.MaxActiveMissiles);
        Assert.AreEqual(zero.MinMissileSpeed, d.MinMissileSpeed, 1e-6f);
        Assert.AreEqual(zero.MaxMissileSpeed, d.MaxMissileSpeed, 1e-6f);
    }
}
