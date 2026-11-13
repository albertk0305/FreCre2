using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// PlayerController 이동/점프 로직 테스트.
/// EditMode에서는 FixedUpdate가 돌지 않으므로 Tick(dt)을 직접 호출해 고정 스텝을 시뮬레이션한다.
/// </summary>
public class PlayerControllerTests
{
    private const float Dt = 0.02f;   // ProjectSettings Fixed Timestep
    private const float Eps = 1e-4f;
    private static readonly Vector2 Center = new Vector2(0.5f, -0.5f);
    private static readonly Vector3 VisualBase = new Vector3(0f, 0.1f, 0f); // 0이 아닌 기준 위치로 복귀 검증

    // v0.5: HalfSize 2.0, radius 0.30 → 중심 ±1.70
    private const float MinX = -1.2f, MaxX = 2.2f, MinY = -2.2f, MaxY = 1.2f;

    private GameObject arenaGo;
    private GameObject playerGo;
    private Transform visual;
    private PlayerInputRouter router;
    private PlayerController player;

    [SetUp]
    public void SetUp()
    {
        arenaGo = new GameObject("Arena");
        var arena = arenaGo.AddComponent<ArenaBounds>();
        arena.Center = Center;
        arena.HalfSize = 2.0f;

        playerGo = new GameObject("Player");
        var body = playerGo.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        var col = playerGo.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.30f;

        visual = new GameObject("Visual").transform;
        visual.SetParent(playerGo.transform, false);
        visual.localPosition = VisualBase;

        router = playerGo.AddComponent<PlayerInputRouter>();
        player = playerGo.AddComponent<PlayerController>();

        var so = new SerializedObject(player);
        so.FindProperty("inputRouter").objectReferenceValue = router;
        so.FindProperty("arenaBounds").objectReferenceValue = arena;
        so.FindProperty("visual").objectReferenceValue = visual;
        so.FindProperty("groundCollider").objectReferenceValue = col;
        so.ApplyModifiedPropertiesWithoutUndo();

        player.ResetPlayer(Center);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(playerGo);
        Object.DestroyImmediate(arenaGo);
    }

    private void Step(int steps, float dt = Dt)
    {
        for (int i = 0; i < steps; i++)
        {
            player.Tick(dt);
        }
    }

    private int StepUntilLanded(int maxSteps = 500)
    {
        int n = 0;
        while (!player.IsGrounded && n < maxSteps)
        {
            player.Tick(Dt);
            n++;
        }
        Assert.IsTrue(player.IsGrounded, "착지하지 않음");
        return n;
    }

    private void PressJump()
    {
        router.SubmitInput(router.MoveVector, true);
    }

    /// <summary>점프 입력 → 첫 스텝에서 이륙 확인 → 착지까지 진행. 반환값은 총 스텝 수.</summary>
    private int JumpAndLand()
    {
        PressJump();
        player.Tick(Dt);
        Assert.IsFalse(player.IsGrounded, "점프가 시작되지 않음");
        return 1 + StepUntilLanded();
    }

    // ---------- 초기값 ----------

    [Test]
    public void InspectorDefaults_MatchBalanceDoc()
    {
        Assert.AreEqual(1.33f, player.MoveSpeed, 1e-6f);
        Assert.AreEqual(2.50f, player.JumpPower, 1e-6f);
        Assert.AreEqual(6.67f, player.Gravity, 1e-6f);
        Assert.AreEqual(1.15f, player.JumpCooldown, 1e-6f);
    }

    [Test]
    public void Reset_InitialState()
    {
        Assert.AreEqual(Center, player.GroundPosition);
        Assert.IsTrue(player.IsGrounded);
        Assert.IsTrue(player.CanControl);
        Assert.IsFalse(player.IsDead);
        Assert.AreEqual(0f, player.JumpHeight);
        Assert.AreEqual(0f, player.VerticalVelocity);
        Assert.AreEqual(Vector2.down, player.FacingDirection);
        Assert.AreEqual(0f, player.JumpCooldownRemaining);
    }

    // ---------- 이동 ----------

    [Test]
    public void Move_UsesMoveSpeedPerSecond()
    {
        router.SubmitInput(Vector2.right, false);
        Step(50); // 1.0초
        Assert.AreEqual(Center.x + 1.33f, player.GroundPosition.x, Eps);
        Assert.AreEqual(Center.y, player.GroundPosition.y, Eps);
    }

    [Test]
    public void Move_DiagonalSpeedIsNormalized()
    {
        // 라우터를 거치지 않은 비정규 입력도 컨트롤러에서 정규화된다.
        player.Move(new Vector2(1f, 1f), 0.5f);
        float dist = (player.GroundPosition - Center).magnitude;
        Assert.AreEqual(1.33f * 0.5f, dist, Eps);
    }

    [Test]
    public void Move_IsFrameRateIndependent()
    {
        router.SubmitInput(Vector2.left, false);
        Step(25, 0.02f); // 0.5초
        Vector2 a = player.GroundPosition;

        player.ResetPlayer(Center);
        router.SubmitInput(Vector2.left, false);
        Step(50, 0.01f); // 0.5초
        Assert.AreEqual(a.x, player.GroundPosition.x, Eps);
    }

    [TestCase(1f, 0f, MaxX, -0.5f)]
    [TestCase(-1f, 0f, MinX, -0.5f)]
    [TestCase(0f, 1f, 0.5f, MaxY)]
    [TestCase(0f, -1f, 0.5f, MinY)]
    [TestCase(1f, 1f, MaxX, MaxY)]
    [TestCase(-1f, 1f, MinX, MaxY)]
    [TestCase(1f, -1f, MaxX, MinY)]
    [TestCase(-1f, -1f, MinX, MinY)]
    public void Move_StaysInsideArena_EdgesAndCorners(float ix, float iy, float ex, float ey)
    {
        router.SubmitInput(new Vector2(ix, iy), false);
        Step(400); // 8초: 어느 방향이든 경계에 충분히 도달
        Assert.AreEqual(ex, player.GroundPosition.x, Eps);
        Assert.AreEqual(ey, player.GroundPosition.y, Eps);
    }

    [Test]
    public void FacingDirection_IsKeptAfterRelease()
    {
        router.SubmitInput(new Vector2(-1f, 1f), false);
        Step(3);
        router.SubmitInput(Vector2.zero, false);
        Step(3);

        Assert.AreEqual(Vector2.zero, player.MoveDirection);
        Assert.AreEqual(-Mathf.Sqrt(0.5f), player.FacingDirection.x, Eps);
        Assert.AreEqual(Mathf.Sqrt(0.5f), player.FacingDirection.y, Eps);
    }

    // ---------- 점프 ----------

    [Test]
    public void Jump_AvailableImmediatelyAtStart()
    {
        Assert.IsTrue(player.TryJump());
    }

    [Test]
    public void Jump_StartsAndRises()
    {
        PressJump();
        Step(1);

        Assert.IsFalse(player.IsGrounded);
        Assert.Greater(player.JumpHeight, 0f);
        Assert.Greater(player.VerticalVelocity, 0f);
        Assert.Less(player.VerticalVelocity, player.JumpPower);
        Assert.AreEqual(player.SimulationTime, player.LastJumpTime, 1e-6f);

        float h1 = player.JumpHeight;
        Step(1);
        Assert.Greater(player.JumpHeight, h1);
    }

    [Test]
    public void Jump_FallsAfterPeak()
    {
        PressJump();
        float expectedPeak = 2.5f * 2.5f / (2f * 6.67f); // ≈ 0.4685
        float maxHeight = 0f;
        bool descended = false;
        float prev = 0f;

        for (int i = 0; i < 200 && (i == 0 || !player.IsGrounded); i++)
        {
            player.Tick(Dt);
            maxHeight = Mathf.Max(maxHeight, player.JumpHeight);
            if (!player.IsGrounded && player.JumpHeight < prev)
            {
                descended = true;
                Assert.Less(player.VerticalVelocity, 0f);
            }
            prev = player.JumpHeight;
        }

        Assert.IsTrue(descended, "최고점 이후 하강하지 않음");
        Assert.AreEqual(expectedPeak, maxHeight, 0.01f);
    }

    [Test]
    public void Jump_LandsAndResetsState()
    {
        int steps = JumpAndLand();

        Assert.AreEqual(0f, player.JumpHeight);
        Assert.AreEqual(0f, player.VerticalVelocity);
        Assert.IsTrue(player.IsGrounded);
        Assert.AreEqual(VisualBase.y, visual.localPosition.y, 1e-6f);

        float airTime = steps * Dt; // 이론값 2v/g ≈ 0.75초
        Assert.AreEqual(2f * 2.5f / 6.67f, airTime, Dt + 1e-4f);
    }

    [Test]
    public void Jump_TrajectoryIsFrameRateIndependent()
    {
        PressJump();
        Step(10, 0.02f); // 0.2초
        float a = player.JumpHeight;

        player.ResetPlayer(Center);
        PressJump();
        Step(20, 0.01f); // 0.2초
        float b = player.JumpHeight;

        float expected = 2.5f * 0.2f - 0.5f * 6.67f * 0.2f * 0.2f;
        Assert.AreEqual(expected, a, Eps);
        Assert.AreEqual(expected, b, Eps);
    }

    [Test]
    public void Jump_CannotJumpInAir()
    {
        PressJump();
        Step(5);
        float v = player.VerticalVelocity;
        float t = player.LastJumpTime;

        Assert.IsFalse(player.TryJump());
        Assert.AreEqual(v, player.VerticalVelocity);
        Assert.AreEqual(t, player.LastJumpTime);

        // 입력 경로로 눌러도 동일
        PressJump();
        Step(1);
        Assert.AreEqual(t, player.LastJumpTime);
    }

    [Test]
    public void Jump_CannotJumpDuringCooldownAfterLanding()
    {
        JumpAndLand();
        Assert.Less(player.SimulationTime - player.LastJumpTime, 1.15f);

        Assert.IsFalse(player.TryJump(), "착지했지만 쿨다운 중");
        Assert.Greater(player.JumpCooldownRemaining, 0f);

        // 1.10초 시점에도 불가
        while (player.SimulationTime - player.LastJumpTime < 1.10f) player.Tick(Dt);
        Assert.IsFalse(player.TryJump());
    }

    [Test]
    public void Jump_AllowedAfterCooldown()
    {
        PressJump();
        Step(1);
        float jumpStart = player.LastJumpTime;

        int guard = 0;
        while (player.JumpCooldownRemaining > 0f && guard++ < 500) player.Tick(Dt);

        Assert.GreaterOrEqual(player.SimulationTime - jumpStart, 1.15f);
        Assert.Less(player.SimulationTime - jumpStart, 1.15f + Dt + 1e-4f);
        Assert.IsTrue(player.TryJump());
        Assert.AreEqual(player.SimulationTime, player.LastJumpTime);
    }

    [Test]
    public void Jump_CanMoveAndTurnWhileAirborne()
    {
        router.SubmitInput(Vector2.right, true);
        Step(5);
        Assert.IsFalse(player.IsGrounded);
        float x1 = player.GroundPosition.x;
        Assert.Greater(x1, Center.x);
        Assert.AreEqual(Vector2.right, player.FacingDirection);

        router.SubmitInput(Vector2.left, false);
        Step(5);
        Assert.IsFalse(player.IsGrounded);
        Assert.Less(player.GroundPosition.x, x1);
        Assert.AreEqual(Vector2.left, player.FacingDirection);
    }

    [Test]
    public void Jump_MovesOnlyVisual_GroundPositionStaysOnGround()
    {
        Vector3 bodyPosBefore = playerGo.transform.position;
        PressJump();
        Step(10);

        Assert.Greater(player.JumpHeight, 0.1f);
        // 지면 좌표와 본체(Collider/Rigidbody2D)는 점프 높이의 영향을 받지 않는다.
        Assert.AreEqual(Center, player.GroundPosition);
        Assert.AreEqual(bodyPosBefore, playerGo.transform.position);
        // Visual만 기준 위치 + JumpHeight
        Assert.AreEqual(VisualBase.x, visual.localPosition.x, 1e-6f);
        Assert.AreEqual(VisualBase.y + player.JumpHeight, visual.localPosition.y, 1e-6f);
        Assert.AreEqual(VisualBase.z, visual.localPosition.z, 1e-6f);
    }

    [Test]
    public void JumpInput_IsConsumedOnce()
    {
        PressJump();
        Step(1);
        Assert.IsFalse(player.IsGrounded);
        Assert.IsFalse(router.JumpPressed, "점프 요청이 소비되지 않음");

        // 추가 입력 없이 착지 + 쿨다운 종료 후에도 자동으로 다시 점프하지 않는다.
        StepUntilLanded();
        Step(40);
        Assert.IsTrue(player.IsGrounded);
        Assert.AreEqual(0f, player.JumpCooldownRemaining);
    }

    [Test]
    public void JumpInput_DuringCooldown_IsDiscardedNotQueued()
    {
        JumpAndLand();
        float firstJump = player.LastJumpTime;

        PressJump(); // 쿨다운 중 입력
        Step(1);
        Assert.IsTrue(player.IsGrounded);

        Step(40); // 쿨다운 종료
        Assert.IsTrue(player.IsGrounded, "쿨다운 중 입력이 버퍼에 남아 나중에 실행됨");
        Assert.AreEqual(firstJump, player.LastJumpTime);
    }

    // ---------- 조작/사망/리셋 ----------

    [Test]
    public void ControlDisabled_BlocksMoveAndJump()
    {
        player.SetControlEnabled(false);
        router.SubmitInput(Vector2.right, true);
        Step(10);

        Assert.AreEqual(Center, player.GroundPosition);
        Assert.IsTrue(player.IsGrounded);

        player.SetControlEnabled(true);
        router.SubmitInput(Vector2.right, false);
        Step(10);
        Assert.Greater(player.GroundPosition.x, Center.x);
    }

    [Test]
    public void Die_BlocksControl_ResetRestores()
    {
        PressJump();
        Step(5);
        player.Die();
        Assert.IsTrue(player.IsDead);
        Assert.IsFalse(player.CanControl);
        player.SetControlEnabled(true);
        Assert.IsFalse(player.CanControl, "사망 중에는 조작 활성화 불가");

        router.SubmitInput(Vector2.right, false);
        StepUntilLanded();
        Assert.AreEqual(Center.x, player.GroundPosition.x, Eps);

        player.ResetPlayer(Center);
        Assert.IsFalse(player.IsDead);
        Assert.IsTrue(player.CanControl);
        Assert.IsTrue(player.TryJump(), "리셋 직후 바로 점프 가능");
    }

    [Test]
    public void Reset_ClampsPositionIntoArena()
    {
        player.ResetPlayer(new Vector2(10f, 10f));
        Assert.AreEqual(MaxX, player.GroundPosition.x, Eps);
        Assert.AreEqual(MaxY, player.GroundPosition.y, Eps);
    }
}
