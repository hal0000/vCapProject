using PrimeTween;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using vCapProject.Scene;

namespace vCapProject.Core
{
    /// <summary>
    ///     Central manager class that handles core game systems, scene management, and resource loading.
    ///     Implements the Singleton pattern for global access to game systems.
    /// </summary>
    [DefaultExecutionOrder(-1500)]
    public class GameManager : MonoBehaviour
    {
        public BaseScene CurrentScene;
        public SceneService SceneService;

        /// <summary>
        ///     Singleton instance of the GameManager for global access.
        /// </summary>
        public static GameManager Instance { get; private set; }

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