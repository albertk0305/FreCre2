using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// 실제 화면(Test Runner Game View 크기)에서 16:9 고정 결과를 검증한다.
/// 어떤 비율에서 실행되어도: 카메라 Viewport 16:9, UI 논리 크기 1920×1080, 버튼 화면 위치가 Viewport 안, 클릭(레이캐스트) 정확, 여백 클릭 무시.
/// </summary>
public class AspectRatioPlayModeTests
{
    private const float Aspect = 16f / 9f;
    private readonly List<GameObject> created = new List<GameObject>();

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (GameObject go in created) if (go != null) Object.Destroy(go);
        created.Clear();
        yield return null;
    }

    private RectTransform AddButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    private static List<RaycastResult> RaycastAt(Vector2 screenPoint)
    {
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPoint }, results);
        return results;
    }

    [UnityTest]
    public IEnumerator CurrentScreen_ViewportIs16By9_UiLayoutAndClicksMatch()
    {
        var root = new GameObject("AspectTestRoot");
        root.SetActive(false);
        created.Add(root);

        var es = new GameObject("EventSystem", typeof(EventSystem));
        created.Add(es);

        var camGo = new GameObject("Main Camera");
        camGo.transform.SetParent(root.transform, false);
        camGo.transform.position = new Vector3(0.5f, -0.5f, -10f);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 2.5f;

        var canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(root.transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Play 씬과 같은 배치(중앙 앵커 + 1920×1080 기준 오프셋)
        RectTransform center = AddButton(canvasGo.transform, "Center", Vector2.zero, new Vector2(200f, 100f));
        RectTransform joystick = AddButton(canvasGo.transform, "MobileJoystickBg", new Vector2(-750f, -330f), new Vector2(280f, 280f));
        RectTransform jump = AddButton(canvasGo.transform, "MobileJumpButton", new Vector2(750f, -330f), new Vector2(200f, 200f));
        RectTransform exit = AddButton(canvasGo.transform, "ExitButton", new Vector2(755f, 230f), new Vector2(300f, 150f));
        RectTransform howTo = AddButton(canvasGo.transform, "HowToPlayButton", new Vector2(-750f, 450f), new Vector2(150f, 50f));

        var controller = camGo.AddComponent<AspectRatioController>();
#if UNITY_EDITOR
        var so = new UnityEditor.SerializedObject(controller);
        SerializedPropertyHelper(so, canvas);
#endif
        root.SetActive(true);
        yield return null;
        yield return null;

        // 1) 카메라 Viewport: 16:9, 화면 안
        Rect px = cam.pixelRect;
        Assert.AreEqual(Aspect, px.width / px.height, 0.01f, $"Viewport 16:9 (Screen {Screen.width}×{Screen.height})");
        Assert.GreaterOrEqual(px.xMin, -0.5f);
        Assert.GreaterOrEqual(px.yMin, -0.5f);
        Assert.LessOrEqual(px.xMax, Screen.width + 0.5f);
        Assert.LessOrEqual(px.yMax, Screen.height + 0.5f);
        Assert.AreEqual(2.5f, cam.orthographicSize);
        Assert.AreEqual(Aspect, cam.aspect, 0.01f, "카메라 기준 제거 경계(MissileSpawner)도 16:9 기준");

        // 2) UI 논리 크기: 항상 1920×1080
        Rect canvasRect = ((RectTransform)canvasGo.transform).rect;
        Assert.AreEqual(1920f, canvasRect.width, 2f, "Canvas 논리 폭");
        Assert.AreEqual(1080f, canvasRect.height, 2f, "Canvas 논리 높이");

        // 3) 각 버튼 중심이 Viewport 안에 있고, 그 위치를 클릭하면 해당 버튼이 맞는다
        foreach (RectTransform rt in new[] { center, joystick, jump, exit, howTo })
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, rt.position);
            Assert.IsTrue(px.Contains(screen), $"{rt.name} 화면 위치 {screen}가 16:9 영역 {px} 안");
            List<RaycastResult> hits = RaycastAt(screen);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject == rt.gameObject, $"{rt.name} 클릭 좌표 정확");

            // 기준 해상도 대비 위치 비율이 화면 비율과 무관하게 같은지
            Vector2 normalized = new Vector2((screen.x - px.x) / px.width, (screen.y - px.y) / px.height);
            Vector2 expected = new Vector2(0.5f + rt.anchoredPosition.x / 1920f, 0.5f + rt.anchoredPosition.y / 1080f);
            Assert.AreEqual(expected.x, normalized.x, 0.01f, $"{rt.name} 상대 X");
            Assert.AreEqual(expected.y, normalized.y, 0.01f, $"{rt.name} 상대 Y");
        }

        // 4) 여백(있을 때만)을 클릭하면 아무 UI도 맞지 않는다
        if (px.xMin >= 2f)
        {
            Assert.IsEmpty(RaycastAt(new Vector2(px.xMin * 0.5f, Screen.height * 0.5f)), "좌측 여백 클릭 무시");
        }
        if (px.yMin >= 2f)
        {
            Assert.IsEmpty(RaycastAt(new Vector2(Screen.width * 0.5f, px.yMin * 0.5f)), "하단 여백 클릭 무시");
        }
    }

#if UNITY_EDITOR
    private static void SerializedPropertyHelper(UnityEditor.SerializedObject so, Canvas canvas)
    {
        UnityEditor.SerializedProperty list = so.FindProperty("canvases");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).objectReferenceValue = canvas;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
#endif

    // ---------- 실제 씬 (Inspector 설정 후) ----------

    [UnityTest]
    public IEnumerator MainAndPlayScenes_UseSame16By9Policy()
    {
        foreach (string sceneName in new[] { SceneNavigator.MainSceneName, SceneNavigator.PlaySceneName })
        {
            int loaded = 0;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> h = (s, _) => { if (s.name == sceneName) loaded++; };
            SceneManager.sceneLoaded += h;
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            for (int i = 0; i < 300 && loaded == 0; i++) yield return null;
            SceneManager.sceneLoaded -= h;
            yield return null;
            yield return null;

            var controller = Object.FindFirstObjectByType<AspectRatioController>();
            if (controller == null)
            {
                Assert.Ignore($"{sceneName} 씬의 Main Camera에 AspectRatioController가 아직 없습니다. Inspector 설정 후 다시 실행하세요.");
            }
            Camera cam = controller.TargetCamera;
            Assert.AreSame(Camera.main, cam, $"{sceneName}: Main Camera에 부착");
            Assert.AreEqual(Aspect, cam.pixelRect.width / cam.pixelRect.height, 0.01f, $"{sceneName}: 16:9");
            Assert.AreEqual(2.5f, cam.orthographicSize, $"{sceneName}: Orthographic Size 유지");

            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas) continue;
                Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode, $"{sceneName}/{canvas.name}: 16:9 영역에 표시");
                Assert.AreSame(cam, canvas.worldCamera, $"{sceneName}/{canvas.name}");
                Rect r = ((RectTransform)canvas.transform).rect;
                Assert.AreEqual(1920f, r.width, 2f, $"{sceneName}/{canvas.name}: 논리 폭");
                Assert.AreEqual(1080f, r.height, 2f, $"{sceneName}/{canvas.name}: 논리 높이");
            }
        }
    }
}
