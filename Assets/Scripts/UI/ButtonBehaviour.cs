using Coffee.UIEffects;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using vCapProject.Core;

namespace vCapProject.UI
{
    [RequireComponent(typeof(UIEffect))]
    public class ButtonBehaviour : UIElement, IPointerEnterHandler, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool ButtonDisabled;
        public bool ContainsIdleEffect;
        public bool NoAnim;
        public bool NoEffect;

        public UnityEvent ClickAction = new();
        public UnityEvent<UnityAction> Disabled = new();
        public UnityEvent<UnityAction> Enabled= new();

        private readonly float _animationDuration = 0.1f;
        private readonly float _scaleDownSize = 0.85f;
        private readonly float _scaleUpSize= 1.15f;

        private Vector3 _originalScale;
        private Tween _scaleTween;
        private UIEffect _uiEffect;

        private bool _interactable = true;

        public override void Awake()
        {
            base.Awake();
            if (!TryGetComponent(out UIEffect temp)) return;
            _uiEffect = temp;
            _uiEffect.samplingScale = ContainsIdleEffect ? 1 : 0;
        }

        private void Start()
        {
            _originalScale = !IsVisible ? Vector3.one : transform.localScale;
            if (ButtonDisabled) Disabled?.Invoke(null);
        }

        public override void Hide()
        {
            _interactable = false;
            ButtonDisabled = true;

            if (_scaleTween.isAlive) _scaleTween.Stop();
            if (!NoEffect && _uiEffect) _uiEffect.samplingScale = 0;

            base.Hide();
        }

        public override void Show()
        {
            base.Show();
            ButtonDisabled = false;
            _interactable = true;

            if (_scaleTween.isAlive) _scaleTween.Stop();
         }
        public void OnPointerClick(PointerEventData data)
        {
            if (ButtonDisabled || !_interactable || !IsVisible) return;
            if (data.button != PointerEventData.InputButton.Left) return;
            SFXManager.I.PlayClick();
            ClickAction.Invoke();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (ButtonDisabled || !_interactable || !IsVisible) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (NoAnim) return;
            if (_scaleTween.isAlive) _scaleTween.Stop();
            _scaleTween = Tween.Scale(transform, transform.localScale, _originalScale * _scaleDownSize, _animationDuration, Ease.OutSine);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (ButtonDisabled || !_interactable || !IsVisible) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (NoAnim) return;
            if (_scaleTween.isAlive) _scaleTween.Stop(); 
            _scaleTween = Tween.Scale(transform, transform.localScale, _originalScale, _animationDuration, Ease.OutSine);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (ButtonDisabled || !_interactable || !IsVisible) return;
            if (!NoAnim)
            {
                if (_scaleTween.isAlive) _scaleTween.Stop();
                _scaleTween = Tween.Scale(transform, transform.localScale, _originalScale * _scaleUpSize, _animationDuration, Ease.OutSine);
            }
            if (!NoEffect && _uiEffect) _uiEffect.samplingScale = 1;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (ButtonDisabled || !_interactable || !IsVisible) return;

            if (!NoAnim)
            {
                if (_scaleTween.isAlive) _scaleTween.Stop();
                _scaleTween = Tween.Scale(transform, transform.localScale, _originalScale, _animationDuration, Ease.OutSine);
            }
            if (!NoEffect && _uiEffect) _uiEffect.samplingScale = 0;
        }

        public void SetDisabled()
        {
            ButtonDisabled = true;
            _interactable = false;
            if (_scaleTween.isAlive) _scaleTween.Stop();
            if (!NoEffect && _uiEffect) _uiEffect.samplingScale = 0;
            Disabled?.Invoke(null);
        }

        public void SetEnabled()
        {
            ButtonDisabled = false;
            _interactable = true;
            Enabled?.Invoke(null);
        }
    }
}