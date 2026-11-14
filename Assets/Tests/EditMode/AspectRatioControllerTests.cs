using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>16:9 화면 비율 고정(레터박스/필러박스) 계산과 카메라/Canvas 적용 테스트 (EditMode).</summary>
public class AspectRatioControllerTests
{
    private const float Aspect = 16f / 9f;

    private static Rect Full(int w, int h) => new Rect(0f, 0f, w, h);

    /// <summary>정규화 Viewport를 픽셀 Rect로.</summary>
    private static Rect ToPixels(Rect viewport, int w, int h)
    {
        return new Rect(viewport.x * w, viewport.y * h, viewport.width * w, viewport.height * h);
    }

    // ---------- 비율별 계산 ----------

    //        width  height  여백(0 없음, 1 좌우, 2 상하)
    [TestCase(1920, 1080, 0)]
    [TestCase(1280, 720, 0)]
    [TestCase(1920, 1200, 2)] // 16:10
    [TestCase(1024, 768, 2)]  // 4:3
    [TestCase(2560, 1080, 1)] // 울트라와이드
    [TestCase(2340, 1080, 1)] // 모바일 가로 19.5:9
    public void ComputeViewport_Keeps16By9_InsideScreen_SymmetricBars(int w, int h, int bars)
    {
        Rect vp = AspectRatioController.ComputeViewport(w, h, Full(w, h), Aspect);
        Rect px = ToPixels(vp, w, h);

        Assert.AreEqual(Aspect, px.width / px.height, 1e-3f, "16:9 유지");
        Assert.GreaterOrEqual(vp.xMin, -1e-6f);
        Assert.GreaterOrEqual(vp.yMin, -1e-6f);
        Assert.LessOrEqual(vp.xMax, 1f + 1e-6f, "화면 밖으로 벗어나지 않음");
        Assert.LessOrEqual(vp.yMax, 1f + 1e-6f);

        float left = px.xMin, right = w - px.xMax, bottom = px.yMin, top = h - px.yMax;
        Assert.AreEqual(left, right, 0.01f, "좌우 여백 대칭");
        Assert.AreEqual(bottom, top, 0.01f, "상하 여백 대칭");

        switch (bars)
        {
            case 0:
                Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), vp, "16:9 화면은 전체 사용");
                break;
            case 1:
                Assert.Greater(left, 0f, "넓은 화면 → 좌우 여백");
                Assert.AreEqual(0f, bottom, 0.01f);
                Assert.AreEqual(h, px.height, 0.01f, "높이를 모두 사용");
                break;
            case 2:
                Assert.Greater(bottom, 0f, "좁은 화면 → 상하 여백");
                Assert.AreEqual(0f, left, 0.01f);
                Assert.AreEqual(w, px.width, 0.01f, "너비를 모두 사용");
                break;
        }
    }

    [Test]
    public void ComputeViewport_ExpectedPixelSizes()
    {
        Rect uw = ToPixels(AspectRatioController.ComputeViewport(2560, 1080, Full(2560, 1080), Aspect), 2560, 1080);
        Assert.AreEqual(1920f, uw.width, 0.01f);
        Assert.AreEqual(320f, uw.x, 0.01f, "21:9 → 좌우 320px");

        Rect ft = ToPixels(AspectRatioController.ComputeViewport(1024, 768, Full(1024, 768), Aspect), 1024, 768);
        Assert.AreEqual(576f, ft.height, 0.01f);
        Assert.AreEqual(96f, ft.y, 0.01f, "4:3 → 상하 96px");
    }

    [TestCase(0, 1080)]
    [TestCase(1920, 0)]
    [TestCase(-1920, 1080)]
    public void ComputeViewport_InvalidScreen_ReturnsFullRect(int w, int h)
    {
        Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), AspectRatioController.ComputeViewport(w, h, Full(1920, 1080), Aspect));
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void ComputeViewport_InvalidAspect_ReturnsFullRect(float aspect)
    {
        Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), AspectRatioController.ComputeViewport(1920, 1080, Full(1920, 1080), aspect));
    }

    [Test]
    public void ComputeViewport_InvalidSafeArea_FallsBackToFullScreen()
    {
        Rect expected = AspectRatioController.ComputeViewport(2560, 1080, Full(2560, 1080), Aspect);
        Assert.AreEqual(expected, AspectRatioController.ComputeViewport(2560, 1080, new Rect(0f, 0f, 0f, 0f), Aspect));
        Assert.AreEqual(expected, AspectRatioController.ComputeViewport(2560, 1080, new Rect(float.NaN, 0f, 10f, 10f), Aspect));
        Assert.AreEqual(expected, AspectRatioController.ComputeViewport(2560, 1080, new Rect(-500f, -500f, 9999f, 9999f), Aspect),
            "화면보다 큰 Safe Area는 화면으로 제한");
    }

    [Test]
    public void ComputeViewport_StaysInsideSafeArea_WithNotch()
    {
        // 2340×1080 가로 화면, 왼쪽 노치 132px, 오른쪽 제스처 영역 66px, 아래 홈 인디케이터 63px
        var safe = new Rect(132f, 63f, 2340f - 132f - 66f, 1080f - 63f);
        Rect px = ToPixels(AspectRatioController.ComputeViewport(2340, 1080, safe, Aspect), 2340, 1080);

        Assert.AreEqual(Aspect, px.width / px.height, 1e-3f);
        Assert.GreaterOrEqual(px.xMin, safe.xMin - 0.01f, "노치 영역 침범 없음");
        Assert.LessOrEqual(px.xMax, safe.xMax + 0.01f);
        Assert.GreaterOrEqual(px.yMin, safe.yMin - 0.01f);
        Assert.LessOrEqual(px.yMax, safe.yMax + 0.01f);
        Assert.AreEqual(safe.height, px.height, 0.01f, "Safe Area 높이를 모두 사용");
        Assert.AreEqual(safe.center.x, px.center.x, 0.01f, "Safe Area 안에서 가운데 정렬");
    }

    [TestCase(1080f, 1f)]
    [TestCase(720f, 0.6666667f)]
    [TestCase(576f, 0.5333333f)]  // 1024×768의 16:9 영역
    [TestCase(1440f, 1.3333334f)]
    public void ComputeCanvasScale_KeepsReferenceLayout(float viewportHeight, float expected)
    {
        Assert.AreEqual(expected, AspectRatioController.ComputeCanvasScale(viewportHeight, 1080f), 1e-5f);
    }

    [TestCase(0f)]
    [TestCase(float.NaN)]
    public void ComputeCanvasScale_Invalid_IsOne(float viewportHeight)
    {
        Assert.AreEqual(1f, AspectRatioController.ComputeCanvasScale(viewportHeight, 1080f));
        Assert.AreEqual(1f, AspectRatioController.ComputeCanvasScale(1080f, 0f));
    }

    // ---------- 컴포넌트 적용 ----------

    private GameObject cameraGo;
    private GameObject canvasGo;

    [TearDown]
    public void TearDown()
    {
        if (cameraGo != null) Object.DestroyImmediate(cameraGo);
        if (canvasGo != null) Object.DestroyImmediate(canvasGo);
    }

    private AspectRatioController CreateController(out Camera cam, out Canvas canvas, out CanvasScaler scaler)
    {
        cameraGo = new GameObject("Main Camera");
        cam = cameraGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 2.5f;
        cam.depth = -1f;
        cameraGo.transform.position = new Vector3(0.5f, -0.5f, -10f);

        // 씬과 같은 설정: Overlay + Scale With Screen Size(1920×1080, Match 0.5)
        canvasGo = new GameObject("UICanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var controller = cameraGo.AddComponent<AspectRatioController>();
        var so = new SerializedObject(controller);
        SerializedProperty list = so.FindProperty("canvases");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).objectReferenceValue = canvas;
        so.ApplyModifiedPropertiesWithoutUndo();

        controller.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
        return controller;
    }

    [Test]
    public void Awake_ConfiguresCanvasToCameraSpace_AndCreatesBlackBackgroundCamera()
    {
        var controller = CreateController(out Camera cam, out Canvas canvas, out CanvasScaler scaler);

        Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode, "UI가 카메라 Viewport(16:9)를 따름");
        Assert.AreSame(cam, canvas.worldCamera);
        Assert.Greater(canvas.planeDistance, cam.nearClipPlane);
        Assert.Less(canvas.planeDistance, 10f, "월드 스프라이트(거리 10)보다 앞");
        Assert.Greater(canvas.sortingOrder, 20, "월드 스프라이트 Order보다 위");
        Assert.AreEqual(CanvasScaler.ScaleMode.ConstantPixelSize, scaler.uiScaleMode);
        Assert.AreEqual(new Vector2(1920f, 1080f), scaler.referenceResolution, "Reference Resolution 유지");

        Camera bg = controller.BackgroundCamera;
        Assert.IsNotNull(bg);
        Assert.AreNotSame(cam, bg);
        Assert.Less(bg.depth, cam.depth, "배경 카메라가 먼저 그려짐");
        Assert.AreEqual(0, bg.cullingMask, "아무것도 렌더링하지 않음");
        Assert.AreEqual(CameraClearFlags.SolidColor, bg.clearFlags);
        Assert.AreEqual(Color.black, bg.backgroundColor);
        Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), bg.rect, "여백 포함 화면 전체를 지움");
        Assert.AreEqual("Untagged", bg.tag, "Camera.main에 잡히지 않음");
    }

    [Test]
    public void Apply_SetsViewportAndCanvasScale_ForEachResolution_WithoutTouchingWorldCamera()
    {
        var controller = CreateController(out Camera cam, out _, out CanvasScaler scaler);

        controller.Apply(2560, 1080, Full(2560, 1080));
        Assert.AreEqual(AspectRatioController.ComputeViewport(2560, 1080, Full(2560, 1080), Aspect), cam.rect);
        Assert.AreEqual(1f, scaler.scaleFactor, 1e-5f, "16:9 영역 높이 1080 → 배율 1");

        controller.Apply(1024, 768, Full(1024, 768));
        Assert.AreEqual(AspectRatioController.ComputeViewport(1024, 768, Full(1024, 768), Aspect), cam.rect);
        Assert.AreEqual(576f / 1080f, scaler.scaleFactor, 1e-5f);

        controller.Apply(1920, 1080, Full(1920, 1080));
        Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), cam.rect);
        Assert.AreEqual(1f, scaler.scaleFactor, 1e-5f);

        Assert.AreEqual(2.5f, cam.orthographicSize, "Orthographic Size 변경 없음");
        Assert.AreEqual(new Vector3(0.5f, -0.5f, -10f), cam.transform.position, "카메라 위치 변경 없음");
    }

    [Test]
    public void Disable_RestoresFullViewport()
    {
        var controller = CreateController(out Camera cam, out _, out _);
        controller.Apply(2560, 1080, Full(2560, 1080));
        controller.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);

        Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), cam.rect);
        Assert.IsFalse(controller.BackgroundCamera.enabled);
    }
}
