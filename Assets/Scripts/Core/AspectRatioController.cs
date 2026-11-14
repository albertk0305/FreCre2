using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 비율과 무관하게 게임 화면과 UI를 16:9 영역에 고정한다(레터박스/필러박스). Main Camera에 붙인다.
/// - 화면(또는 Screen.safeArea) 안에 들어가는 가장 큰 16:9 영역을 계산해 Camera.rect(Viewport Rect)로 설정한다.
///   넓은 화면은 좌우, 좁은 화면은 상하에 검은 여백이 생긴다. Orthographic Size, 카메라 위치, 월드 좌표는 바꾸지 않는다.
///   (Camera.aspect도 16:9가 되므로 MissileSpawner의 카메라 기준 제거 경계도 기존과 같다.)
/// - 여백은 이 카메라보다 먼저 그려지는 배경 카메라(검은색, 아무것도 렌더링하지 않음)로 지운다.
/// - UI: 연결한 Canvas를 실행 시 Screen Space - Camera로 이 카메라에 붙여 같은 16:9 영역에만 그리고,
///   Canvas 배율을 (16:9 영역 높이 / Reference Resolution 높이)로 고정한다. 기존 1920×1080 배치가 그대로 유지된다.
///   레이캐스트도 카메라 Viewport 밖(여백)은 무시하므로 여백 터치/클릭은 UI·조이스틱 입력이 되지 않는다.
/// - 화면 크기, 창 크기, Safe Area가 바뀌면 다시 계산한다(Editor Game View의 Free Aspect로 확인 가능).
/// 해상도 자체(Screen.SetResolution)는 바꾸지 않는다. 씬 파일의 Canvas 설정(Overlay)은 Edit 모드에서 그대로다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(-100)] // UI 컴포넌트(Awake)와 EventSystem보다 먼저 화면 영역을 확정
public class AspectRatioController : MonoBehaviour
{
    [Header("Target Aspect")]
    [Min(0.01f)] [SerializeField] private float targetWidth = 16f;
    [Min(0.01f)] [SerializeField] private float targetHeight = 9f;
    [Tooltip("모바일 노치/카메라 홀을 피하도록 Screen.safeArea 안에서 16:9 영역을 계산한다. PC에서는 safeArea = 전체 화면.")]
    [SerializeField] private bool respectSafeArea = true;

    [Header("Letterbox")]
    [SerializeField] private Color barColor = Color.black;
    [Tooltip("선택. 여백을 지울 배경 카메라. 비우면 실행 시 자식으로 자동 생성한다.")]
    [SerializeField] private Camera backgroundCamera;

    [Header("UI")]
    [Tooltip("16:9 영역에 맞출 Canvas (Main: Canvas, Play: UICanvas). 실행 시 Screen Space - Camera로 전환된다.")]
    [SerializeField] private Canvas[] canvases = new Canvas[0];
    [Tooltip("Canvas와 카메라 사이 거리. 카메라 Near Clip보다 크고 월드(카메라에서 10)보다 가까워야 한다.")]
    [Min(0.01f)] [SerializeField] private float canvasPlaneDistance = 1f;
    [Tooltip("UI가 월드 스프라이트(Order 최대 20)보다 항상 위에 그려지도록 하는 Canvas Order in Layer.")]
    [SerializeField] private int canvasSortingOrder = 1000;

    private Camera targetCamera;
    private bool canvasesConfigured;
    private float[] referenceHeights = new float[0];
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private Rect lastSafeArea;

    /// <summary>현재 적용 중인 정규화 Viewport Rect.</summary>
    public Rect CurrentViewport { get; private set; } = new Rect(0f, 0f, 1f, 1f);

    public float TargetAspect => targetWidth / targetHeight;
    public Camera TargetCamera => targetCamera;
    public Camera BackgroundCamera => backgroundCamera;

    private void Awake()
    {
        targetCamera = GetComponent<Camera>();
        EnsureBackgroundCamera();
        ConfigureCanvases();
        Refresh(force: true);
    }

    private void OnEnable()
    {
        if (backgroundCamera != null)
        {
            backgroundCamera.enabled = true;
        }
        Refresh(force: true);
    }

    private void OnDisable()
    {
        if (targetCamera != null)
        {
            targetCamera.rect = new Rect(0f, 0f, 1f, 1f);
        }
        if (backgroundCamera != null)
        {
            backgroundCamera.enabled = false;
        }
    }

    private void Update()
    {
        Refresh(force: false);
    }

    /// <summary>화면 크기/Safe Area가 바뀌었으면(또는 force) 다시 계산해 적용한다.</summary>
    public void Refresh(bool force)
    {
        int width = Screen.width;
        int height = Screen.height;
        Rect safeArea = respectSafeArea ? Screen.safeArea : new Rect(0f, 0f, width, height);
        if (!force && width == lastScreenWidth && height == lastScreenHeight && safeArea == lastSafeArea)
        {
            return;
        }
        lastScreenWidth = width;
        lastScreenHeight = height;
        lastSafeArea = safeArea;
        Apply(width, height, safeArea);
    }

    /// <summary>주어진 화면 크기(px)와 사용 가능 영역(px)으로 Viewport와 Canvas 배율을 적용한다. 테스트에서 직접 호출할 수 있다.</summary>
    public void Apply(int screenWidth, int screenHeight, Rect usableArea)
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        CurrentViewport = ComputeViewport(screenWidth, screenHeight, usableArea, TargetAspect);
        targetCamera.rect = CurrentViewport;

        if (backgroundCamera != null)
        {
            backgroundCamera.depth = targetCamera.depth - 1f;
            backgroundCamera.backgroundColor = barColor;
        }

        float viewportPixelHeight = screenWidth > 0 && screenHeight > 0 ? CurrentViewport.height * screenHeight : 0f;
        ApplyCanvasScale(viewportPixelHeight);
    }

    private void EnsureBackgroundCamera()
    {
        if (backgroundCamera == null)
        {
            var go = new GameObject("LetterboxBackgroundCamera");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            backgroundCamera = go.AddComponent<Camera>();
        }

        backgroundCamera.rect = new Rect(0f, 0f, 1f, 1f);
        backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
        backgroundCamera.backgroundColor = barColor;
        backgroundCamera.cullingMask = 0; // 아무것도 렌더링하지 않고 화면 전체를 지우기만 한다
        backgroundCamera.orthographic = true;
        backgroundCamera.depth = (targetCamera != null ? targetCamera.depth : 0f) - 1f;
        backgroundCamera.useOcclusionCulling = false;
        var listener = backgroundCamera.GetComponent<AudioListener>();
        if (listener != null)
        {
            listener.enabled = false;
        }
    }

    private void ConfigureCanvases()
    {
        if (canvasesConfigured || canvases == null)
        {
            return;
        }
        canvasesConfigured = true;
        referenceHeights = new float[canvases.Length];

        float planeDistance = Mathf.Max(canvasPlaneDistance, targetCamera.nearClipPlane + 0.01f);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
            {
                continue;
            }
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = targetCamera;
            canvas.planeDistance = planeDistance;
            canvas.sortingOrder = canvasSortingOrder;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                referenceHeights[i] = scaler.referenceResolution.y; // 기존 Reference Resolution(1920×1080) 기준 유지
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            }
        }
    }

    private void ApplyCanvasScale(float viewportPixelHeight)
    {
        if (canvases == null)
        {
            return;
        }
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || i >= referenceHeights.Length || referenceHeights[i] <= 0f)
            {
                continue;
            }
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.scaleFactor = ComputeCanvasScale(viewportPixelHeight, referenceHeights[i]);
            }
        }
    }

    // ---------------- 순수 계산 (EditMode 테스트 대상) ----------------

    /// <summary>
    /// 화면(screenWidth×screenHeight px) 안의 사용 가능 영역(usableArea px, 보통 Screen.safeArea)에 들어가는
    /// 가장 큰 targetAspect 영역을 가운데 정렬해 정규화 Viewport Rect(0~1)로 반환한다.
    /// 잘못된 입력(0 이하/NaN/무한대)이면 전체 화면(0,0,1,1). 사용 가능 영역이 잘못되었으면 전체 화면 기준으로 계산한다.
    /// </summary>
    public static Rect ComputeViewport(int screenWidth, int screenHeight, Rect usableArea, float targetAspect)
    {
        var full = new Rect(0f, 0f, 1f, 1f);
        if (screenWidth <= 0 || screenHeight <= 0 || !IsPositiveFinite(targetAspect))
        {
            return full;
        }

        var screen = new Rect(0f, 0f, screenWidth, screenHeight);
        Rect area = ClampToScreen(usableArea, screen);
        if (!IsPositiveFinite(area.width) || !IsPositiveFinite(area.height))
        {
            area = screen;
        }

        float width;
        float height;
        if (area.width / area.height > targetAspect)
        {
            height = area.height;            // 화면이 더 넓음 → 좌우 여백
            width = height * targetAspect;
        }
        else
        {
            width = area.width;              // 화면이 더 좁음(또는 같음) → 상하 여백
            height = width / targetAspect;
        }

        float x = area.x + (area.width - width) * 0.5f;
        float y = area.y + (area.height - height) * 0.5f;
        return new Rect(x / screenWidth, y / screenHeight, width / screenWidth, height / screenHeight);
    }

    /// <summary>16:9 영역 높이(px)에서 기준 해상도 높이의 UI 배치가 그대로 보이도록 하는 Canvas 배율.</summary>
    public static float ComputeCanvasScale(float viewportPixelHeight, float referenceHeight)
    {
        if (!IsPositiveFinite(viewportPixelHeight) || !IsPositiveFinite(referenceHeight))
        {
            return 1f;
        }
        return viewportPixelHeight / referenceHeight;
    }

    private static Rect ClampToScreen(Rect area, Rect screen)
    {
        if (float.IsNaN(area.x) || float.IsNaN(area.y) || float.IsNaN(area.width) || float.IsNaN(area.height))
        {
            return screen;
        }
        float xMin = Mathf.Clamp(area.xMin, screen.xMin, screen.xMax);
        float yMin = Mathf.Clamp(area.yMin, screen.yMin, screen.yMax);
        float xMax = Mathf.Clamp(area.xMax, screen.xMin, screen.xMax);
        float yMax = Mathf.Clamp(area.yMax, screen.yMin, screen.yMax);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
