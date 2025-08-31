using Coffee.UIEffects;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace vCapProject.UI
{
    [RequireComponent(typeof(UIEffect))]
    public class ButtonBehaviour : UIElement, IPointerEnterHandler, IPointerClickHandler, IPointerDownHandler,
        IPointerUpHandler, IPointerExitHandler
    {
        public bool ButtonDisabled;
        public bool ContainsIdleEffect;
        public bool NoAnim;
        public bool NoEffect;

        public UnityEvent ClickAction = new();
        public UnityEvent<UnityAction> Disabled = new();
        public UnityEvent<UnityAction> Enabled = new();
        private readonly float _animationDuration = 0.1f;
        private readonly float _scaleDownSize = 0.85f;
        private readonly float _scaleUpSize = 1.15f;
        private Coroutine _holdCoroutine;
        private Vector3 _originalScale;
        private Tween _scaleTween;
        private UIEffect _uiEffect;

        public override void Awake()
        {
            base.Awake();
            if (!transform.TryGetComponent<UIEffect>(out var temp)) return;
            _uiEffect = temp;
            _uiEffect.samplingScale = ContainsIdleEffect ? 1 : 0;
        }

        private void Start()
        {
            _originalScale = !IsVisible ? Vector3.one : transform.localScale;
            if (ButtonDisabled)
                Disabled?.Invoke(null);
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (ButtonDisabled) return;
            if (data.button == PointerEventData.InputButton.Left) ClickAction.Invoke();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (ButtonDisabled) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (ButtonDisabled) return;
            if (!NoAnim)
            {
                _scaleTween.Complete();
                var tr = transform;
                _scaleTween = Tween.Scale(tr, tr.localScale, _originalScale * _scaleUpSize, _animationDuration,
                    Ease.OutSine);
            }

            if (!NoEffect) _uiEffect.samplingScale = 1;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (ButtonDisabled) return;
            if (!NoAnim)
            {
                _scaleTween.Complete();
                var tr = transform;
                _scaleTween = Tween.Scale(tr, tr.localScale, _originalScale, _animationDuration, Ease.OutSine);
            }

            if (!NoEffect) _uiEffect.samplingScale = 0;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (ButtonDisabled) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (!NoAnim)
            {
                _scaleTween.Complete();
                var tr = transform;
                _scaleTween = Tween.Scale(tr, tr.localScale, _originalScale, _animationDuration, Ease.OutSine);
            }

            if (!NoEffect) _uiEffect.samplingScale = 0;
        }

        public void SetDisabled()
        {
            ButtonDisabled = true;
            Disabled?.Invoke(null);
        }

        public void SetEnabled()
        {
            ButtonDisabled = false;
            Enabled?.Invoke(null);
        }
    }
}