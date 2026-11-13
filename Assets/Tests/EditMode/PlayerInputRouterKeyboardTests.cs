using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>Input System 가상 키보드로 WASD / 방향키 / Space 처리를 검증한다.</summary>
public class PlayerInputRouterKeyboardTests : InputTestFixture
{
    private const float Eps = 1e-5f;
    private static readonly float D = Mathf.Sqrt(0.5f);

    private Keyboard keyboard;
    private GameObject go;
    private PlayerInputRouter router;

    public override void Setup()
    {
        base.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        go = new GameObject("KeyboardRouterTest");
        router = go.AddComponent<PlayerInputRouter>();
        router.Initialize(ControlMode.PC);
    }

    public override void TearDown()
    {
        Object.DestroyImmediate(go);
        base.TearDown();
    }

    private static void AssertVec(Vector2 expected, Vector2 actual)
    {
        Assert.AreEqual(expected.x, actual.x, Eps, "x");
        Assert.AreEqual(expected.y, actual.y, Eps, "y");
    }

    private ButtonControl Key(string name)
    {
        return (ButtonControl)keyboard[name];
    }

    [TestCase("w", 0f, 1f)]
    [TestCase("a", -1f, 0f)]
    [TestCase("s", 0f, -1f)]
    [TestCase("d", 1f, 0f)]
    [TestCase("upArrow", 0f, 1f)]
    [TestCase("leftArrow", -1f, 0f)]
    [TestCase("downArrow", 0f, -1f)]
    [TestCase("rightArrow", 1f, 0f)]
    public void SingleKey_MovesInFourDirections(string key, float ex, float ey)
    {
        Press(Key(key));
        router.UpdateInput();
        AssertVec(new Vector2(ex, ey), router.MoveVector);

        Release(Key(key));
        router.UpdateInput();
        AssertVec(Vector2.zero, router.MoveVector);
    }

    [TestCase("w", "d", 1f, 1f)]
    [TestCase("w", "a", -1f, 1f)]
    [TestCase("s", "d", 1f, -1f)]
    [TestCase("s", "a", -1f, -1f)]
    [TestCase("upArrow", "rightArrow", 1f, 1f)]
    [TestCase("downArrow", "leftArrow", -1f, -1f)]
    [TestCase("w", "rightArrow", 1f, 1f)] // WASD와 방향키 혼용
    public void TwoKeys_MoveDiagonallyNormalized(string k1, string k2, float sx, float sy)
    {
        Press(Key(k1));
        Press(Key(k2));
        router.UpdateInput();
        AssertVec(new Vector2(sx * D, sy * D), router.MoveVector);
        Assert.AreEqual(1f, router.MoveVector.magnitude, Eps);
    }

    [Test]
    public void WAndUpArrowTogether_DoNotDoubleSpeed()
    {
        Press(keyboard.wKey);
        Press(keyboard.upArrowKey);
        router.UpdateInput();
        AssertVec(Vector2.up, router.MoveVector);
    }

    [Test]
    public void Space_ProducesSingleJumpRequestWhileHeld()
    {
        Press(keyboard.spaceKey);
        router.UpdateInput();
        Assert.IsTrue(router.ConsumeJumpPressed());

        // 계속 누르고 있는 동안 다음 프레임에는 새 요청이 생기지 않는다 (자동 재점프 방지).
        InputSystem.Update();
        router.UpdateInput();
        Assert.IsFalse(router.ConsumeJumpPressed());

        Release(keyboard.spaceKey);
        router.UpdateInput();
        Assert.IsFalse(router.ConsumeJumpPressed());

        Press(keyboard.spaceKey);
        router.UpdateInput();
        Assert.IsTrue(router.ConsumeJumpPressed());
    }

    [Test]
    public void DisabledInput_IgnoresKeyboard()
    {
        router.SetInputEnabled(false);
        Press(keyboard.dKey);
        Press(keyboard.spaceKey);
        router.UpdateInput();

        AssertVec(Vector2.zero, router.MoveVector);
        Assert.IsFalse(router.ConsumeJumpPressed());
    }
}
