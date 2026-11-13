using NUnit.Framework;
using UnityEngine;

/// <summary>장치와 무관한 입력 처리(정규화, 점프 버퍼, 활성화) 테스트.</summary>
public class PlayerInputRouterTests
{
    private const float Eps = 1e-5f;
    private static readonly float D = Mathf.Sqrt(0.5f); // 대각선 정규화 성분 ≈ 0.7071

    private GameObject go;
    private PlayerInputRouter router;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("InputRouterTest");
        router = go.AddComponent<PlayerInputRouter>();
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

    //            left   right  down   up     ex   ey
    [TestCase(false, false, false, false, 0f, 0f)]
    [TestCase(false, false, false, true, 0f, 1f)]
    [TestCase(false, false, true, false, 0f, -1f)]
    [TestCase(true, false, false, false, -1f, 0f)]
    [TestCase(false, true, false, false, 1f, 0f)]
    [TestCase(true, true, false, false, 0f, 0f)]   // 좌우 상쇄
    [TestCase(false, false, true, true, 0f, 0f)]   // 상하 상쇄
    public void ComputeMoveVector_FourDirectionsAndCancel(bool l, bool r, bool d, bool u, float ex, float ey)
    {
        AssertVec(new Vector2(ex, ey), PlayerInputRouter.ComputeMoveVector(l, r, d, u));
    }

    [TestCase(false, true, false, true, 1f, 1f)]    // 우상
    [TestCase(true, false, false, true, -1f, 1f)]   // 좌상
    [TestCase(false, true, true, false, 1f, -1f)]   // 우하
    [TestCase(true, false, true, false, -1f, -1f)]  // 좌하
    public void ComputeMoveVector_DiagonalsAreNormalized(bool l, bool r, bool d, bool u, float sx, float sy)
    {
        Vector2 v = PlayerInputRouter.ComputeMoveVector(l, r, d, u);
        Assert.AreEqual(1f, v.magnitude, Eps);
        AssertVec(new Vector2(sx * D, sy * D), v);
    }

    [Test]
    public void SubmitInput_NormalizesDiagonalAndKeepsAnalogMagnitude()
    {
        router.SubmitInput(new Vector2(1f, 1f), false);
        Assert.AreEqual(1f, router.MoveVector.magnitude, Eps);

        router.SubmitInput(new Vector2(0.3f, 0f), false);
        AssertVec(new Vector2(0.3f, 0f), router.MoveVector);
    }

    [Test]
    public void SubmitInput_NaN_BecomesZero()
    {
        router.SubmitInput(new Vector2(float.NaN, 1f), false);
        AssertVec(Vector2.zero, router.MoveVector);
    }

    [Test]
    public void ConsumeJumpPressed_ReturnsTrueExactlyOnce()
    {
        router.SubmitInput(Vector2.zero, true);
        Assert.IsTrue(router.ConsumeJumpPressed());
        Assert.IsFalse(router.ConsumeJumpPressed());
    }

    [Test]
    public void JumpRequest_IsBufferedUntilConsumed()
    {
        // 점프 입력 프레임 이후 여러 Update가 지나도 물리 스텝에서 소비될 때까지 유지된다.
        router.SubmitInput(Vector2.zero, true);
        router.SubmitInput(Vector2.right, false);
        router.SubmitInput(Vector2.right, false);
        Assert.IsTrue(router.JumpPressed);
        Assert.IsTrue(router.ConsumeJumpPressed());
        Assert.IsFalse(router.JumpPressed);
    }

    [Test]
    public void SetInputEnabledFalse_ClearsAndIgnoresInput()
    {
        router.SubmitInput(Vector2.up, true);
        router.SetInputEnabled(false);

        AssertVec(Vector2.zero, router.MoveVector);
        Assert.IsFalse(router.ConsumeJumpPressed());

        router.SubmitInput(Vector2.left, true);
        AssertVec(Vector2.zero, router.MoveVector);
        Assert.IsFalse(router.ConsumeJumpPressed());

        router.SetInputEnabled(true);
        router.SubmitInput(Vector2.left, true);
        AssertVec(Vector2.left, router.MoveVector);
        Assert.IsTrue(router.ConsumeJumpPressed());
    }

    [Test]
    public void Initialize_SetsControlMode()
    {
        router.Initialize(ControlMode.Mobile);
        Assert.AreEqual(ControlMode.Mobile, router.CurrentControlMode);
        router.Initialize(ControlMode.PC);
        Assert.AreEqual(ControlMode.PC, router.CurrentControlMode);
    }
}
