using Reptile;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TamaRush
{
    public class TamaRushPlayMode : MonoBehaviour
    {
        public static bool IsActive { get; private set; }
        public static TamaRushPlayMode Instance { get; private set; }
        public static System.Action OnExitPlayMode;

        private GameObject _canvasGo;
        private TextMeshProUGUI _label;

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void EnsureUICreated()
        {
            if (_canvasGo != null) return;

            var uiManager = Core.Instance?.UIManager?.transform;
            if (uiManager == null) return;

            _canvasGo = new GameObject("TamaRush_PlayModeHint");
            _canvasGo.transform.SetParent(uiManager, false);

            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            _canvasGo.AddComponent<CanvasScaler>();
            _canvasGo.AddComponent<GraphicRaycaster>();

            var textGo = new GameObject("HintLabel");
            textGo.transform.SetParent(_canvasGo.transform, false);

            _label = textGo.AddComponent<TextMeshProUGUI>();
            _label.text = "-Dance Button To Exit Play Mode";
            _label.fontSize = 22;
            _label.color = Color.green;
            _label.alignment = TextAlignmentOptions.TopRight;
            _label.enableWordWrapping = true;

            var rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(400f, 80f);
            rect.anchoredPosition = new Vector2(-16f, -10f);

            var controlsGo = new GameObject("ControlsLabel");
            controlsGo.transform.SetParent(_canvasGo.transform, false);

            var controls = controlsGo.AddComponent<TextMeshProUGUI>();
            controls.text = "-Phone Left: Left Button\n-Phone Up: Center Button\n-Phone Right: Right Button\n-Phone Down: Tap";
            controls.fontSize = 20;
            controls.color = Color.yellow;
            controls.alignment = TextAlignmentOptions.TopRight;
            controls.enableWordWrapping = false;

            var controlsRect = controlsGo.GetComponent<RectTransform>();
            controlsRect.anchorMin = new Vector2(1f, 1f);
            controlsRect.anchorMax = new Vector2(1f, 1f);
            controlsRect.pivot = new Vector2(1f, 1f);
            controlsRect.sizeDelta = new Vector2(400f, 100f);
            controlsRect.anchoredPosition = new Vector2(-16f, -90f);

            _canvasGo.SetActive(false);
        }

        public static void Enter()
        {
            IsActive = true;
            Instance?.EnsureUICreated();
            if (Instance?._canvasGo != null)
                Instance._canvasGo.SetActive(true);
        }

        public static void Exit()
        {
            if (!IsActive) return;
            IsActive = false;
            if (Instance?._canvasGo != null)
                Instance._canvasGo.SetActive(false);
            OnExitPlayMode?.Invoke();
        }

        private void Update()
        {
            if (!IsActive) return;
            var player = WorldHandler.instance?.GetCurrentPlayer();
            if (player == null) return;
            if (player.danceButtonNew)
                Exit();
        }
    }
}
