using NUnit.Framework;
using UnityEngine;

public class ArenaBoundsTests
{
    private const float Eps = 1e-5f;
    private const float Radius = 0.30f;

    // 아래 계산 테스트는 기본값과 무관한 고정 HalfSize(1.65)를 사용한다 → 기대값 limit = 1.35.
    // 기본값(v0.5 = 2.0) 자체는 DefaultHalfSize_Is200, v0.5 실제 설정은 V05Config_* 테스트에서 검증한다.
    private const float MathHalfSize = 1.65f;

    private GameObject go;
    private ArenaBounds arena;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("ArenaBoundsTest");
        arena = go.AddComponent<ArenaBounds>();
        arena.Center = Vector2.zero;
        arena.HalfSize = MathHalfSize;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(go);
    }

    private static void AssertVec(Vector2 expected, Vector2 actual)
    {
        Assert.AreEqual(expected.x, actual.x, Eps, "x");
        Assert.AreEqual(expected.y, actual.y, Eps, "y");
    }

    [Test]
    public void DefaultHalfSize_Is200()
    {
        var fresh = new GameObject("Fresh").AddComponent<ArenaBounds>();
        try { Assert.AreEqual(2.0f, fresh.HalfSize, Eps); }
        finally { Object.DestroyImmediate(fresh.gameObject); }
    }

    // v0.5 씬 설정: Center (0.5,-0.5), HalfSize 2.0, radius 0.30 → X [-1.2, 2.2], Y [-2.2, 1.2]
    [TestCase(10f, -0.5f, 2.2f, -0.5f)]
    [TestCase(-10f, -0.5f, -1.2f, -0.5f)]
    [TestCase(0.5f, 10f, 0.5f, 1.2f)]
    [TestCase(0.5f, -10f, 0.5f, -2.2f)]
    [TestCase(10f, 10f, 2.2f, 1.2f)]
    [TestCase(-10f, 10f, -1.2f, 1.2f)]
    [TestCase(10f, -10f, 2.2f, -2.2f)]
    [TestCase(-10f, -10f, -1.2f, -2.2f)]
    public void V05Config_EdgesAndCorners(float px, float py, float ex, float ey)
    {
        arena.Center = new Vector2(0.5f, -0.5f);
        arena.HalfSize = 2.0f;
        AssertVec(new Vector2(ex, ey), arena.ClampPosition(new Vector2(px, py), Radius));
    }

    [Test]
    public void InsidePosition_IsUnchanged()
    {
        AssertVec(new Vector2(0.5f, -1.0f), arena.ClampPosition(new Vector2(0.5f, -1.0f), Radius));
    }

    [TestCase(10f, 0f, 1.35f, 0f)]     // 오른쪽
    [TestCase(-10f, 0f, -1.35f, 0f)]   // 왼쪽
    [TestCase(0f, 10f, 0f, 1.35f)]     // 위
    [TestCase(0f, -10f, 0f, -1.35f)]   // 아래
    public void FourEdges_AreClamped(float px, float py, float ex, float ey)
    {
        AssertVec(new Vector2(ex, ey), arena.ClampPosition(new Vector2(px, py), Radius));
    }

    [TestCase(5f, 5f, 1.35f, 1.35f)]
    [TestCase(-5f, 5f, -1.35f, 1.35f)]
    [TestCase(5f, -5f, 1.35f, -1.35f)]
    [TestCase(-5f, -5f, -1.35f, -1.35f)]
    public void FourCorners_AreClamped(float px, float py, float ex, float ey)
    {
        AssertVec(new Vector2(ex, ey), arena.ClampPosition(new Vector2(px, py), Radius));
    }

    [Test]
    public void OneAxisOutside_OnlyThatAxisIsClamped()
    {
        AssertVec(new Vector2(1.35f, 0.7f), arena.ClampPosition(new Vector2(3f, 0.7f), Radius));
    }

    [Test]
    public void ExactBoundary_IsKept()
    {
        AssertVec(new Vector2(1.35f, -1.35f), arena.ClampPosition(new Vector2(1.35f, -1.35f), Radius));
    }

    [Test]
    public void PlayerRadius_IsApplied()
    {
        AssertVec(new Vector2(1.65f, 0f), arena.ClampPosition(new Vector2(9f, 0f), 0f));
        AssertVec(new Vector2(1.15f, 0f), arena.ClampPosition(new Vector2(9f, 0f), 0.5f));
    }

    [Test]
    public void NonZeroCenter_IsRespected()
    {
        arena.Center = new Vector2(0.5f, -0.5f);
        AssertVec(new Vector2(1.85f, -1.85f), arena.ClampPosition(new Vector2(10f, -10f), Radius));
        AssertVec(new Vector2(-0.85f, 0.85f), arena.ClampPosition(new Vector2(-10f, 10f), Radius));
        AssertVec(new Vector2(0.5f, -0.5f), arena.ClampPosition(new Vector2(0.5f, -0.5f), Radius));
    }

    [Test]
    public void NegativeRadius_TreatedAsZero()
    {
        AssertVec(new Vector2(1.65f, 0f), arena.ClampPosition(new Vector2(9f, 0f), -1f));
    }

    [Test]
    public void NaNRadius_TreatedAsZero()
    {
        AssertVec(new Vector2(1.65f, 0f), arena.ClampPosition(new Vector2(9f, 0f), float.NaN));
    }

    [Test]
    public void RadiusLargerThanHalfSize_ClampsToCenter()
    {
        arena.Center = new Vector2(0.5f, -0.5f);
        AssertVec(new Vector2(0.5f, -0.5f), arena.ClampPosition(new Vector2(3f, 3f), 5f));
    }

    [Test]
    public void NegativeOrNaNHalfSize_TreatedAsZero()
    {
        arena.HalfSize = -2f;
        Assert.AreEqual(0f, arena.HalfSize, Eps);
        AssertVec(Vector2.zero, arena.ClampPosition(new Vector2(3f, -3f), Radius));

        arena.HalfSize = float.NaN;
        Assert.AreEqual(0f, arena.HalfSize, Eps);
        AssertVec(Vector2.zero, arena.ClampPosition(new Vector2(3f, -3f), Radius));
    }

    [Test]
    public void NaNPosition_FallsBackToCenterOnThatAxis()
    {
        arena.Center = new Vector2(0.5f, -0.5f);
        AssertVec(new Vector2(0.5f, 0.85f), arena.ClampPosition(new Vector2(float.NaN, 9f), Radius));
    }

    [Test]
    public void InfinitePosition_IsClampedToEdge()
    {
        AssertVec(new Vector2(1.35f, -1.35f),
            arena.ClampPosition(new Vector2(float.PositiveInfinity, float.NegativeInfinity), Radius));
    }
}
