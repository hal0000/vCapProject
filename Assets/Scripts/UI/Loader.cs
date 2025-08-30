using Core;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UI
{
    [DefaultExecutionOrder(-1001)]
    public class Loader : UIElement
    {

        public Image ProgressFill;

        public TMP_Text StatusText;

        [Min(0f)] public float progressSmoothTime = 0.15f;

        [Header("Sliced Offsets (Inspector)")] [Tooltip("Constant Left offset in px.")]
        public float Left = 2f;

        [Tooltip("Constant Top offset in px.")]
        public float Top = 2f;

        [Tooltip("Constant Bottom offset in px.")]
        public float Bottom = 2f;

        [Tooltip("Right offset when progress = 1 (full).")]
        public float RightAt100 = 2f;

        [Tooltip("Right offset when progress = 0 (empty).")]
        public float RightAt0 = 150f;

        public UnityEvent OnError;
        public UnityEvent OnSuccess;

        private Tween _fadeTween;
        private Tween _progressTween;
        private float _visualProgress;
        private RectTransform _meter;

        public override void Awake()
        {
            base.Awake();
            if (ProgressFill == null)
            {
                LoggerExtra.LogError("[Loader] ProgressFill reference missing.");
                enabled = false;
                return;
            }

            _meter = ProgressFill.rectTransform;

            if (ProgressFill.type != Image.Type.Sliced)
            {
                LoggerExtra.LogWarning("[Loader] ProgressFill Image.Type is not Sliced. Switching to Sliced.");
                ProgressFill.type = Image.Type.Sliced;
            }

            _visualProgress = 0f;
            ApplyOffsets(0f);
            SetStatus(string.Empty);
        }

        private void OnEnable()
        {
            EventManager.OnLoaderStatusChanged += HandleLoaderStatus;
            EventManager.OnLoadProgress += HandleLoadProgress;
            EventManager.OnLoadProgressDetailed += HandleLoadProgressDetailed;
        }

        private void HandleLoadProgressDetailed(Enums.LoaderPhase phase, float progress01, string message)
        {
            SmoothProgressTo(progress01);
            if (string.IsNullOrEmpty(message)) return;
            switch (phase)
            {
                case Enums.LoaderPhase.DownloadingDependencies:
                    SetStatus($"Downloading… {message}");
                    break;
                case Enums.LoaderPhase.SceneLoading:
                    SetStatus($"Loading scene… {message}");
                    break;
            }
        }

        private void OnDisable()
        {
            EventManager.OnLoaderStatusChanged -= HandleLoaderStatus;
            EventManager.OnLoadProgress -= HandleLoadProgress;
            EventManager.OnLoadProgressDetailed -= HandleLoadProgressDetailed;

            if (_fadeTween.isAlive) _fadeTween.Stop();
            if (_progressTween.isAlive) _progressTween.Stop();
            _visualProgress = 0f;
            ApplyOffsets(0f);
            SetStatus(string.Empty);
        }
        private void HandleLoaderStatus(Enums.LoaderStatus status)
        {
            switch (status)
            {
                case Enums.LoaderStatus.Starting:
                    SetStatus("Loading…");
                    Show();
                    SmoothProgressTo(0f);
                    break;

                case Enums.LoaderStatus.Connecting:
                    SetStatus("Connecting…");
                    Show();
                    break;

                case Enums.LoaderStatus.Downloading:
                    SetStatus("Downloading assets…");
                    Show();
                    break;

                case Enums.LoaderStatus.Retrying:
                    SetStatus("Retrying…");
                    Show();
                    break;

                case Enums.LoaderStatus.NoSpace:
                    SetStatus("Not enough space");
                    Show();
                    break;

                case Enums.LoaderStatus.Paused:
                    SetStatus("Download paused");
                    Show();
                    break;

                case Enums.LoaderStatus.NoInternet:
                    SetStatus("No internet connection");
                    Show();
                    break;

                case Enums.LoaderStatus.Error:
                    SetStatus("Error during load");
                    Show();
                    OnError?.Invoke();
                    break;

                case Enums.LoaderStatus.Completed:
                    SetStatus("Download complete.");
                    Hide();
                    OnSuccess?.Invoke();
                    break;
                case Enums.LoaderStatus.AttemptingToDownloadCatalog:
                    SetStatus("Attempting to Download Catalog.");
                    Show();
                    break;
                case Enums.LoaderStatus.DownloadignCatalogFailed:
                    SetStatus("Downloading Catalog Failed.");
                    Show();
                    break;
                case Enums.LoaderStatus.DownloadingCatalogSuccessful:
                    SetStatus("Downloading Catalog completed.");
                    Show();
                    break;
                default:
                    SetStatus("");
                    Hide();
                    break;
            }
        }

        private void HandleLoadProgress(Enums.LoaderPhase _ignoredPhase, float progress01)
        {
            SmoothProgressTo(progress01);
        }

        public override void Show()
        {
            base.Show();

        }
        public override void Hide()
        {
            base.Hide();

        }

        private void SmoothProgressTo(float target01)
        {
            target01 = Mathf.Clamp01(target01);
            if (Mathf.Abs(target01 - _visualProgress) < 0.004f) return;
            if (_progressTween.isAlive) _progressTween.Stop();

            _progressTween = Tween.Custom(this, _visualProgress, target01, progressSmoothTime, static (Loader self, float v) =>
            {
                self._visualProgress = v;
                self.ApplyOffsets(v);
            });
        }

        private void ApplyOffsets(float progress01)
        {
            var right = Mathf.Lerp(RightAt0, RightAt100, progress01);
            var min = _meter.offsetMin; // (Left, Bottom)
            min.x = Left;
            min.y = Bottom;
            _meter.offsetMin = min;
            var max = _meter.offsetMax; // (-Right, -Top)
            max.x = -right;
            max.y = -Top;
            _meter.offsetMax = max;
        }

        private void SetStatus(string text)
        {
            if (StatusText != null) StatusText.text = text ?? string.Empty;
        }
    }
}