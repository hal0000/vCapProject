using PrimeTween;
using Scene;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

namespace Core
{
    /// <summary>
    ///     Central manager class that handles core game systems, scene management, and resource loading.
    ///     Implements the Singleton pattern for global access to game systems.
    /// </summary>
    [DefaultExecutionOrder(-1500)]
    public class GameManager : MonoBehaviour
    {
        /// <summary>
        ///     Singleton instance of the GameManager for global access.
        /// </summary>
        public static GameManager Instance { get; private set; }

        public BaseScene CurrentScene;
        public SceneService SceneService;

        private void Awake()
        {
            Application.targetFrameRate = 120;
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                EnhancedTouchSupport.Enable();
                TouchSimulation.Enable();
                PrimeTweenConfig.warnZeroDuration = false;
            }
        }
    }
}