using PrimeTween;
using UnityEngine;
using UnityEngine.UI;
using vCapProject.Core;
using vCapProject.Scene;

namespace vCapProject.UI 
{
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]

    public sealed class ThemeTag : MonoBehaviour 
    {
        public Enums.ThemeSlot Slot = Enums.ThemeSlot.UI;
        public Graphic TargetGraphic;
        [Header("Behavior")]
        public bool PreserveAlpha = true;
        public float TweenDuration = 0.12f;
        Tween _colorTween;
        private static ThemeManager _tm;
        void Awake() 
        {
            if (TargetGraphic == null) 
            {
                TryGetComponent(out TargetGraphic);
            }
            if (_tm == null && GameManager.Instance.CurrentScene is MenuScene scene) _tm = scene.ThemeManager;
        }

        void OnEnable() 
        {
            ThemeRegistry.Register(this);
            if (_tm.Current != null) 
            {
                _tm.ApplyToTag(this, 0f);
            }
        }

        void OnDisable() 
        {
            ThemeRegistry.Unregister(this);
            if (_colorTween.isAlive) _colorTween.Stop();
        }

        internal void ApplyColor(Color target, float duration) 
        {
            if (PreserveAlpha) 
            {
                if (TargetGraphic) target.a = TargetGraphic.color.a;
            }

            if (_colorTween.isAlive) _colorTween.Stop();

            if (duration <= 0f) 
            {
                if (TargetGraphic) TargetGraphic.color = target;
                return;
            }
            var from = TargetGraphic.color;
            _colorTween = Tween.Custom(
                this,
                0f, 1f,
                TweenDuration,
                (ThemeTag self, float t) => {
                    var c = Color.Lerp(from, target, t);
                    self.TargetGraphic.color = c;
                },
                Ease.OutSine
            );
        }
    }
}
