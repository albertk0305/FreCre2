using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Stage 10 모바일 조작 테스트 (EditMode).
/// 실제 터치 대신 PointerEventData로 UI 이벤트(OnPointerDown/OnDrag/OnPointerUp)를 직접 전달한다.
/// Screen Space Overlay와 같이 카메라 없이(null) 화면 좌표 = Canvas 월드 좌표로 계산한다.
/// </summary>
public class MobileInputTests
{
    private const float Eps = 1e-4f;
    private const float Dt = 0.02f;
    private static readonly float D = Mathf.Sqrt(0.5f);
    private static readonly Vector2 RestPos = new Vector2(50f, 20f); // Background/손잡이의 기존 배치(0이 아닌 위치로 복귀 검증)

    private GameObject root;
    private RectTransform background;
    private RectTransform handle;
    private MobileJoystick joystick;
    private MobileJumpButton jumpButton;
    private bool hadControlMode;
    private int savedControlMode;

    [SetUp]
    public void SetUp()
    {
        hadControlMode = PlayerPrefs.HasKey(ControlSettings.PlayerPrefsKey);
        savedControlMode = PlayerPrefs.GetInt(ControlSettings.PlayerPrefsKey, 0);

        root = new GameObject("MobileControls", typeof(RectTransform));
        var joystickGo = new GameObject("MobileJoystick", typeof(RectTransform));
        joystickGo.transform.SetParent(root.transform, false);

        background = new GameObject("Background", typeof(RectTransform)).GetComponent<RectTransform>();
        background.SetParent(joystickGo.transform, false);
        background.sizeDelta = new Vector2(280f, 280f); // 씬과 같은 크기 → 기본 반경 140
        background.anchoredPosition = RestPos;
        background.gameObject.AddComponent<Image>();

        handle = new GameObject("JoystickButton", typeof(RectTransform)).GetComponent<RectTransform>();
        handle.SetParent(joystickGo.transform, false);
        handle.sizeDelta = new Vector2(120f, 120f);
        handle.anchoredPosition = RestPos;

        joystick = joystickGo.AddComponent<MobileJoystick>();
        var so = new SerializedObject(joystick);
        so.FindProperty("background").objectReferenceValue = background;
        so.FindProperty("handle").objectReferenceValue = handle;
        so.ApplyModifiedPropertiesWithoutUndo();

        var jumpGo = new GameObject("MobileJumpButton", typeof(RectTransform));
        jumpGo.transform.SetParent(root.transform, false);
        jumpButton = jumpGo.AddComponent<MobileJumpButton>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        if (hadControlMode) PlayerPrefs.SetInt(ControlSettings.PlayerPrefsKey, savedControlMode);
        else PlayerPrefs.DeleteKey(ControlSettings.PlayerPrefsKey);
        PlayerPrefs.Save();
    }

    private static void AssertVec(Vector2 expected, Vector2 actual, string message = null)
    {
        Assert.AreEqual(expected.x, actual.x, Eps, message);
        Assert.AreEqual(expected.y, actual.y, Eps, message);
    }

    /// <summary>Background 중심 기준 오프셋 위치의 포인터 이벤트.</summary>
    private PointerEventData Pointer(int id, Vector2 offsetFromCenter)
    {
        return new PointerEventData(null) { pointerId = id, position = (Vector2)background.position + offsetFromCenter };
    }

    private Vector2 HandleOffset => (Vector2)handle.localPosition - RestPos;

    // ---------- 8방향 변환 ----------

    //          각도   기대 x  기대 y
    [TestCase(0f, 1f, 0f)]
    [TestCase(22f, 1f, 0f)]
    [TestCase(23f, 1f, 1f)]
    [TestCase(45f, 1f, 1f)]
    [TestCase(67f, 1f, 1f)]
    [TestCase(68f, 0f, 1f)]
    [TestCase(90f, 0f, 1f)]
    [TestCase(135f, -1f, 1f)]
    [TestCase(180f, -1f, 0f)]
    [TestCase(-180f, -1f, 0f)]
    [TestCase(-157f, -1f, -1f)]
    [TestCase(-135f, -1f, -1f)]
    [TestCase(-90f, 0f, -1f)]
    [TestCase(-67f, 1f, -1f)]
    [TestCase(-68f, 0f, -1f)]
    [TestCase(-22f, 1f, 0f)]
    public void ComputeDirection_SnapsToNearest45Degrees(float angle, float sx, float sy)
    {
        float rad = angle * Mathf.Deg2Rad;
        Vector2 offset = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 100f;

        Vector2 dir = MobileJoystick.ComputeDirection(offset, 140f, 0.2f);

        Vector2 expected = new Vector2(sx, sy).normalized;
        AssertVec(expected, dir);
        Assert.AreEqual(1f, dir.magnitude, Eps, "Dead Zone 밖에서는 크기 1 (대각선도 정규화)");
    }

    [Test]
    public void ComputeDirection_DeadZoneIs20PercentOfRadius()
    {
        // 반경 100 → Dead Zone 20
        AssertVec(Vector2.zero, MobileJoystick.ComputeDirection(Vector2.zero, 100f, 0.2f));
        AssertVec(Vector2.zero, MobileJoystick.ComputeDirection(new Vector2(19.9f, 0f), 100f, 0.2f));
        AssertVec(Vector2.zero, MobileJoystick.ComputeDirection(new Vector2(14f, 14f), 100f, 0.2f)); // 길이 19.8
        AssertVec(Vector2.right, MobileJoystick.ComputeDirection(new Vector2(20.5f, 0f), 100f, 0.2f));
        AssertVec(Vector2.right, MobileJoystick.ComputeDirection(new Vector2(500f, 0f), 100f, 0.2f), "반경 밖도 크기 1");
    }

    [Test]
    public void ComputeDirection_InvalidRadiusOrNaN_IsZero()
    {
        AssertVec(Vector2.zero, MobileJoystick.ComputeDirection(Vector2.right * 50f, 0f, 0.2f));
        AssertVec(Vector2.zero, MobileJoystick.ComputeDirection(new Vector2(float.NaN, 1f), 100f, 0.2f));
    }

    [Test]
    public void ClampHandleOffset_LimitsToRadius()
    {
        AssertVec(new Vector2(30f, 40f), MobileJoystick.ClampHandleOffset(new Vector2(30f, 40f), 100f));
        Assert.AreEqual(100f, MobileJoystick.ClampHandleOffset(new Vector2(300f, 400f), 100f).magnitude, Eps);
    }

    // ---------- 조이스틱 포인터 처리 ----------

    [Test]
    public void Joystick_DefaultRadiusIsHalfBackgroundWidth()
    {
        Assert.AreEqual(140f, joystick.MovementRadius, Eps);
        Assert.AreEqual(0.2f, joystick.DeadZoneRatio, Eps);
    }

    [Test]
    public void Joystick_DownDragUp_UpdatesDirectionAndHandle_ThenResets()
    {
        joystick.OnPointerDown(Pointer(1, new Vector2(100f, 0f)));
        Assert.IsTrue(joystick.IsPressed);
        AssertVec(Vector2.right, joystick.Direction);
        AssertVec(new Vector2(100f, 0f), HandleOffset, "손잡이는 실제 드래그 위치");

        joystick.OnDrag(Pointer(1, new Vector2(400f, 400f)));
        AssertVec(new Vector2(D, D), joystick.Direction);
        Assert.AreEqual(140f, HandleOffset.magnitude, 1e-2f, "손잡이는 최대 반경 내");

        joystick.OnDrag(Pointer(1, new Vector2(10f, -10f))); // Dead Zone(28) 안
        AssertVec(Vector2.zero, joystick.Direction);

        joystick.OnPointerUp(Pointer(1, new Vector2(-100f, 0f)));
        Assert.IsFalse(joystick.IsPressed);
        AssertVec(Vector2.zero, joystick.Direction, "터치 종료 시 입력 초기화");
        AssertVec(Vector2.zero, HandleOffset, "터치 종료 시 중앙 복귀");
    }

    [Test]
    public void Joystick_OtherPointer_DoesNotInterfere()
    {
        joystick.OnPointerDown(Pointer(1, new Vector2(0f, 100f)));

        // 두 번째 손가락: 조이스틱 위에 닿아도 무시
        joystick.OnPointerDown(Pointer(2, new Vector2(-100f, 0f)));
        joystick.OnDrag(Pointer(2, new Vector2(0f, -100f)));
        AssertVec(Vector2.up, joystick.Direction);
        joystick.OnPointerUp(Pointer(2, Vector2.zero));
        Assert.IsTrue(joystick.IsPressed, "다른 손가락을 떼도 조이스틱 유지");
        AssertVec(Vector2.up, joystick.Direction);
        Assert.AreEqual(1, joystick.ActivePointerId);

        // 원래 손가락 드래그는 계속 반영
        joystick.OnDrag(Pointer(1, new Vector2(-100f, 0f)));
        AssertVec(Vector2.left, joystick.Direction);
    }

    [Test]
    public void Joystick_ResetInput_ClearsStateForInterruptedTouch()
    {
        joystick.OnPointerDown(Pointer(1, new Vector2(100f, 0f)));
        joystick.ResetInput(); // OnDisable / 포커스 상실 시 호출되는 경로

        AssertVec(Vector2.zero, joystick.Direction);
        AssertVec(Vector2.zero, HandleOffset);
        joystick.OnDrag(Pointer(1, new Vector2(0f, 100f)));
        AssertVec(Vector2.zero, joystick.Direction, "끊긴 터치의 드래그는 새 입력을 만들지 않음");

        joystick.OnPointerDown(Pointer(3, new Vector2(0f, 100f)));
        AssertVec(Vector2.up, joystick.Direction, "새 터치는 정상 처리");
    }

    // ---------- 점프 버튼 ----------

    [Test]
    public void JumpButton_PressRegistersSingleRequest_HoldDoesNotRepeat()
    {
        Assert.IsFalse(jumpButton.ConsumeJumpPressed());

        jumpButton.OnPointerDown(new PointerEventData(null) { pointerId = 5 });
        Assert.IsTrue(jumpButton.IsPressed);
        Assert.IsTrue(jumpButton.ConsumeJumpPressed());
        Assert.IsFalse(jumpButton.ConsumeJumpPressed(), "누르고 있어도 다시 요청하지 않음");

        jumpButton.OnPointerUp(new PointerEventData(null) { pointerId = 5 });
        Assert.IsFalse(jumpButton.IsPressed);
        Assert.IsFalse(jumpButton.ConsumeJumpPressed());

        jumpButton.OnPointerDown(new PointerEventData(null) { pointerId = 6 });
        Assert.IsTrue(jumpButton.ConsumeJumpPressed(), "다시 누르면 새 요청");
    }

    // ---------- PlayerInputRouter 통합 ----------

    private PlayerInputRouter CreateRouter(ControlMode mode)
    {
        var router = root.AddComponent<PlayerInputRouter>();
        router.SetMobileInputs(joystick, jumpButton);
        router.Initialize(mode);
        return router;
    }

    [Test]
    public void Router_MobileMode_UsesJoystickAndJumpButton()
    {
        var router = CreateRouter(ControlMode.Mobile);

        joystick.OnPointerDown(Pointer(1, new Vector2(-100f, 100f)));
        jumpButton.OnPointerDown(new PointerEventData(null) { pointerId = 2 }); // 동시 멀티터치
        router.UpdateInput();

        AssertVec(new Vector2(-D, D), router.MoveVector);
        Assert.IsTrue(router.JumpPressed);
        Assert.IsTrue(router.ConsumeJumpPressed());

        router.UpdateInput(); // 점프 버튼을 계속 누르고 있음
        Assert.IsFalse(router.ConsumeJumpPressed(), "자동 재점프 없음");
        AssertVec(new Vector2(-D, D), router.MoveVector, "점프 버튼이 조이스틱 입력을 방해하지 않음");

        joystick.OnPointerUp(Pointer(1, Vector2.zero));
        router.UpdateInput();
        AssertVec(Vector2.zero, router.MoveVector, "터치 해제 시 이동 상태가 남지 않음");
    }

    [Test]
    public void Router_PCMode_IgnoresMobileInputs()
    {
        var router = CreateRouter(ControlMode.PC);

        joystick.OnPointerDown(Pointer(1, new Vector2(100f, 0f)));
        router.UpdateInput();

        Assert.AreNotEqual(Vector2.right, router.MoveVector);
    }

    [Test]
    public void Router_InputDisabled_DiscardsMobileJump()
    {
        var router = CreateRouter(ControlMode.Mobile);
        router.SetInputEnabled(false);

        jumpButton.OnPointerDown(new PointerEventData(null) { pointerId = 2 });
        joystick.OnPointerDown(Pointer(1, new Vector2(100f, 0f)));
        router.UpdateInput();
        Assert.IsFalse(router.ConsumeJumpPressed());
        AssertVec(Vector2.zero, router.MoveVector);

        router.SetInputEnabled(true);
        router.UpdateInput();
        Assert.IsFalse(router.ConsumeJumpPressed(), "차단 중 누른 점프가 나중에 실행되지 않음");
        AssertVec(Vector2.right, router.MoveVector, "다시 활성화되면 현재 조이스틱 상태 반영");
    }

    [Test]
    public void Router_MobileModeWithoutMobileInputs_FallsBackToKeyboardPath()
    {
        var go = new GameObject("RouterNoMobile");
        go.transform.SetParent(root.transform, false);
        var router = go.AddComponent<PlayerInputRouter>();
        router.Initialize(ControlMode.Mobile);

        Assert.DoesNotThrow(() => router.UpdateInput());
        Assert.AreEqual(ControlMode.Mobile, router.CurrentControlMode);
    }

    [Test]
    public void Router_Initialize_StillTogglesMobileControlsRoot()
    {
        var router = root.AddComponent<PlayerInputRouter>();
        var so = new SerializedObject(router);
        so.FindProperty("mobileControlsRoot").objectReferenceValue = joystick.gameObject;
        so.ApplyModifiedPropertiesWithoutUndo();

        router.Initialize(ControlMode.PC);
        Assert.IsFalse(joystick.gameObject.activeSelf);
        router.Initialize(ControlMode.Mobile);
        Assert.IsTrue(joystick.gameObject.activeSelf);
    }

    // ---------- PlayerController와 연결 (점프 조건/쿨다운 유지) ----------

    [Test]
    public void MobileJump_UsesExistingPlayerJumpRules()
    {
        var arenaGo = new GameObject("Arena");
        arenaGo.transform.SetParent(root.transform, false);
        var arena = arenaGo.AddComponent<ArenaBounds>();
        arena.Center = new Vector2(0.5f, -0.5f);
        arena.HalfSize = 2f;

        var playerGo = new GameObject("Player");
        playerGo.transform.SetParent(root.transform, false);
        playerGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        var col = playerGo.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.3f;
        new GameObject("Visual").transform.SetParent(playerGo.transform, false);
        var router = playerGo.AddComponent<PlayerInputRouter>();
        router.SetMobileInputs(joystick, jumpButton);
        router.Initialize(ControlMode.Mobile);
        var player = playerGo.AddComponent<PlayerController>();
        var so = new SerializedObject(player);
        so.FindProperty("inputRouter").objectReferenceValue = router;
        so.FindProperty("arenaBounds").objectReferenceValue = arena;
        so.FindProperty("groundCollider").objectReferenceValue = col;
        so.ApplyModifiedPropertiesWithoutUndo();
        player.ResetPlayer(arena.Center);

        // 탭 → 점프
        jumpButton.OnPointerDown(new PointerEventData(null) { pointerId = 2 });
        router.UpdateInput();
        player.Tick(Dt);
        Assert.IsFalse(player.IsGrounded, "모바일 점프 버튼으로 점프");

        // 계속 누른 채 3초: 착지 후 쿨다운이 지나도 자동 재점프 없음
        int jumpsWhileHeld = 0;
        bool wasGrounded = false;
        for (float t = 0f; t < 3f; t += Dt)
        {
            router.UpdateInput();
            player.Tick(Dt);
            if (wasGrounded && !player.IsGrounded) jumpsWhileHeld++;
            wasGrounded = player.IsGrounded;
        }
        Assert.IsTrue(player.IsGrounded);
        Assert.AreEqual(0, jumpsWhileHeld);

        // 떼고 다시 누르면 점프
        jumpButton.OnPointerUp(new PointerEventData(null) { pointerId = 2 });
        jumpButton.OnPointerDown(new PointerEventData(null) { pointerId = 3 });
        router.UpdateInput();
        player.Tick(Dt);
        Assert.IsFalse(player.IsGrounded);

        // 공중에서 다시 누름 → 기존 규칙대로 무시(요청이 쌓이지 않음)
        jumpButton.OnPointerDown(new PointerEventData(null) { pointerId = 4 });
        router.UpdateInput();
        player.Tick(Dt);
        for (int i = 0; i < 200 && !player.IsGrounded; i++)
        {
            router.UpdateInput();
            player.Tick(Dt);
        }
        Assert.IsTrue(player.IsGrounded);
        for (int i = 0; i < 5; i++)
        {
            router.UpdateInput();
            player.Tick(Dt);
        }
        Assert.IsTrue(player.IsGrounded, "공중에서 누른 점프가 착지 후 실행되지 않음");
    }
}
