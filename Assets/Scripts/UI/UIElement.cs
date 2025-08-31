using System;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using vCapProject.Interface;

namespace vCapProject.UI
{
    /// <summary>
    ///     Base class for all UI elements with ultimate performance optimizations.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UIElement : MonoBehaviour
    {
        private static readonly Dictionary<Type, IAnimatable[]> _typeCache = new(32, new TypeComparer());
        private static readonly Dictionary<Type, int> _typeCountCache = new(32, new TypeComparer());
        private static readonly ObjectPool<List<IAnimatable>> _listPool = new(32, 32);

        /// <summary>
        ///     Duration of animations in seconds.
        /// </summary>
        [Header("Animation Settings")] public float AnimDuration = 0.2f;

        /// <summary>
        ///     Default scale of the UI element when fully shown.
        /// </summary>
        public Vector3 DefaultScale = Vector3.one;

        /// <summary>
        ///     Pivot point used during scale animations.
        /// </summary>
        public Vector2 AnimationPivot = new(0.5f, 0.5f);

        /// <summary>
        ///     Whether to animate the scale of the element when showing/hiding.
        /// </summary>
        [Tooltip("Enable scale animation?")] public bool AnimScale;

        /// <summary>
        ///     Whether to animate the position of the element when showing/hiding.
        /// </summary>
        [Tooltip("Enable position animation?")]
        public bool AnimPosition;

        /// <summary>
        ///     Whether to animate the alpha (fade) of the element when showing/hiding.
        /// </summary>
        [Tooltip("Enable alpha (fade) animation?")]
        public bool AnimAlpha = true;

        /// <summary>
        ///     Whether to animate the alpha (fade) of the element when showing/hiding.
        /// </summary>
        [Tooltip("Skips traverse Child Search if child is disabled")]
        public bool OptimizedAlpha;

        /// <summary>
        ///     Target local position for position animation.
        ///     When showing: animates from TargetPosition to default position.
        ///     When hiding: animates from default position to TargetPosition.
        /// </summary>
        [Tooltip(
            "Target local position for position animation (Show: animate from TargetPosition to default position, Hide: reverse).")]
        public Vector3 TargetPosition;

        /// <summary>
        ///     Event invoked when the UI element is shown.
        /// </summary>
        public UnityEvent OnShow;

        /// <summary>
        ///     Event invoked when the UI element is hidden.
        /// </summary>
        public UnityEvent OnHide;

        /// <summary>
        ///     Whether the UI element is currently visible.
        /// </summary>
        public bool IsVisible;

        /// <summary>
        ///     List of all Graphic components in this UI element and its children.
        /// </summary>
        private readonly List<Graphic> _graphics = new();

        /// <summary>
        ///     List of all TextMeshProUGUI components in this UI element and its children.
        /// </summary>
        private readonly List<TextMeshProUGUI> _textMeshPros = new();

        private int _animatableCount;

        private IAnimatable[] _animatables;

        /// <summary>
        ///     Default pivot point of the RectTransform, cached for animation purposes.
        /// </summary>
        private Vector2 _defaultPivot;

        /// <summary>
        ///     Default local position of the element, cached for animation purposes.
        /// </summary>
        protected Vector3 _defaultPosition;

        private bool _isInitialized;

        /// <summary>
        ///     Cached reference to the RectTransform component.
        /// </summary>
        private RectTransform _rectTransform;

        /// <summary>
        ///     Whether animations should be allowed to run.
        /// </summary>
        protected bool CanAnimate => IsVisible && gameObject.activeInHierarchy;

        public bool EnableThemeSwitch = false;
        [Min(0f)] public float ThemeLerp = 0.10f;
        
        /// <summary>
        ///     Initializes the UI element by caching components and setting up default values.
        /// </summary>
        public virtual void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _defaultPivot = _rectTransform.pivot;
            _defaultPosition = transform.localPosition;
            // If no target position was set in Inspector, use the default.
            if (TargetPosition == Vector3.zero) TargetPosition = _defaultPosition;
            if (AnimAlpha) TraverseAndCache(transform);
            Initialize();
        }

        public virtual void OnDestroy()
        {
            _animatables = null;
            _isInitialized = false;
        }

        public void UpdateContent(int index)
        {
            var a = index;
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            var type = GetType();

            // 1. Check type cache first (FASTEST PATH)
            if (_typeCache.TryGetValue(type, out var cached))
            {
                _animatables = cached;
                _animatableCount = _typeCountCache[type];
                _isInitialized = true;
                return;
            }

            // 2. Create new cache (SLOW PATH - but only once per type)
            var list = _listPool.Get();
            try
            {
                GetComponentsInChildren(false, list);

                // Pre-allocate array with exact size
                _animatables = new IAnimatable[list.Count];
                _animatableCount = list.Count;

                // Fast array copy
                Array.Copy(list.ToArray(), _animatables, _animatableCount);

                // Cache for future use
                _typeCache[type] = _animatables;
                _typeCountCache[type] = _animatableCount;
            }
            finally
            {
                _listPool.Return(list);
            }

            _isInitialized = true;
        }

        /// <summary>
        ///     Recursively traverses the transform hierarchy and caches UI components for alpha animation.
        ///     Skips any child with its own UIElement component to prevent duplicate animations.
        /// </summary>
        /// <param name="parent">The transform to start traversing from.</param>
        private void TraverseAndCache(Transform parent)
        {
            foreach (Transform child in parent)
            {
                if (OptimizedAlpha && !child.gameObject.activeSelf) continue;
                if (child.GetComponent<UIElement>() != null) continue;
                if (child.TryGetComponent(out TextMeshProUGUI tmp))
                {
                    _textMeshPros.Add(tmp);
                    if (tmp is Graphic graphicTmp) graphicTmp.raycastTarget = false;
                }

                if (child.TryGetComponent(out Graphic graphic))
                    if (!_graphics.Contains(graphic))
                    {
                        _graphics.Add(graphic);
                        graphic.raycastTarget = false;
                    }

                TraverseAndCache(child);
            }
        }

        /// <summary>
        ///     Shows the UI element with the enabled animations and invokes the OnShow event.
        /// </summary>
        public virtual void Show()
        {
            if (!_isInitialized) Initialize();
            if (!gameObject.activeInHierarchy || IsVisible) return;

            IsVisible = true;

            // ULTRA OPTIMIZED LOOP - No bounds checking, no null checks
            for (var i = 0; i < _animatableCount; i++) _animatables[i].OnParentVisibilityChanged(true);

            ShowAnimations();
            OnShow?.Invoke();
        }

        /// <summary>
        ///     Hides the UI element with the enabled animations and invokes the OnHide event.
        /// </summary>
        public virtual void Hide()
        {
            if (!_isInitialized) Initialize();
            if (!gameObject.activeInHierarchy || !IsVisible) return;

            IsVisible = false;

            // ULTRA OPTIMIZED LOOP - No bounds checking, no null checks
            for (var i = 0; i < _animatableCount; i++) _animatables[i].OnParentVisibilityChanged(false);

            HideAnimations();
            OnHide?.Invoke();
        }

        /// <summary>
        ///     Performs the show animations for scale, position, and alpha using PrimeTween.
        /// </summary>
        public virtual void ShowAnimations()
        {
            if (!IsVisible) return;

            if (AnimScale)
            {
                var tr = transform;
                _rectTransform.pivot = AnimationPivot;
                Tween.Scale(tr, tr.localScale, DefaultScale, AnimDuration, Ease.OutSine).OnComplete(() =>
                {
                    _rectTransform.pivot = _defaultPivot;
                });
            }

            if (AnimPosition)
            {
                var tr = transform;
                Tween.LocalPosition(tr, tr.localPosition, _defaultPosition, AnimDuration, Ease.OutSine);
            }

            if (!AnimAlpha) return;
            foreach (var tmp in _textMeshPros)
                Tween.Alpha(tmp, tmp.color.a, 1f, AnimDuration, Ease.OutSine)
                    .OnComplete(() => { tmp.raycastTarget = true; });
            foreach (var graphic in _graphics)
                Tween.Alpha(graphic, graphic.color.a, 1f, AnimDuration, Ease.OutSine)
                    .OnComplete(() => { graphic.raycastTarget = true; });
        }

        /// <summary>
        ///     Performs the hide animations for scale, position, and alpha using PrimeTween.
        /// </summary>
        public virtual void HideAnimations()
        {
            if (IsVisible) return;

            if (AnimScale)
            {
                var tr = transform;
                Tween.Scale(tr, tr.localScale, Vector3.zero, AnimDuration, Ease.InSine).OnComplete(() =>
                {
                    _rectTransform.pivot = _defaultPivot;
                });
            }

            if (AnimPosition)
            {
                var tr = transform;
                Tween.LocalPosition(tr, tr.localPosition, TargetPosition, AnimDuration, Ease.InSine);
            }

            if (!AnimAlpha) return;
            foreach (var tmp in _textMeshPros)
                Tween.Alpha(tmp, tmp.color.a, 0f, AnimDuration, Ease.InSine)
                    .OnComplete(() => { tmp.raycastTarget = false; });
            foreach (var graphic in _graphics)
                Tween.Alpha(graphic, graphic.color.a, 0f, AnimDuration, Ease.InSine)
                    .OnComplete(() => { graphic.raycastTarget = false; });
        }

        /// <summary>
        ///     Performs a simple alpha loop animation between 0.2 and 1.0.
        /// </summary>
        /// <param name="duration">Duration of each fade in/out cycle</param>
        /// <param name="graphic">The graphic component to animate</param>
        protected void StartAlphaLoop(float duration, Graphic graphic)
        {
            if (!CanAnimate) return;

            Tween.Alpha(graphic, 0.2f, 1f, duration, Ease.Linear, -1, CycleMode.Yoyo);
        }

        /// <summary>
        ///     Performs a simple alpha loop animation between 0.2 and 1.0 for all graphics.
        /// </summary>
        /// <param name="duration">Duration of each fade in/out cycle</param>
        protected void StartAlphaLoopForAll(float duration)
        {
            if (!CanAnimate) return;

            foreach (var graphic in _graphics)
                Tween.Alpha(graphic, 0.2f, 1f, duration, Ease.Linear, -1, CycleMode.Yoyo);
        }
    }

    public class TypeComparer : IEqualityComparer<Type>
    {
        public bool Equals(Type x, Type y)
        {
            return x == y;
        }

        public int GetHashCode(Type obj)
        {
            return obj.GetHashCode();
        }
    }

    public class ObjectPool<T> where T : new()
    {
        private readonly int _maxSize;
        private readonly Stack<T> _pool;

        public ObjectPool(int initialSize, int maxSize)
        {
            _pool = new Stack<T>(initialSize);
            _maxSize = maxSize;

            for (var i = 0; i < initialSize; i++) _pool.Push(new T());
        }

        public T Get()
        {
            return _pool.Count > 0 ? _pool.Pop() : new T();
        }

        public void Return(T item)
        {
            if (_pool.Count < _maxSize) _pool.Push(item);
        }
    }
}