using System;

namespace Core
{
    /// <summary>
    ///     Minimal event hub for the event flow.
    /// </summary>
    public static class EventManager
    {
        public static event Action<Enums.Notification> OnNewNotification;

        public static void NewNotificationInvoke(Enums.Notification n)
        {
            OnNewNotification?.Invoke(n);
        }

        public static event Action<Enums.LoaderPhase, float> OnLoadProgress;
        public static event Action<Enums.LoaderPhase, float, string> OnLoadProgressDetailed;

        public static void LoadProgressInvoke(Enums.LoaderPhase phase, float p)
        {
            OnLoadProgress?.Invoke(phase, p);
            OnLoadProgressDetailed?.Invoke(phase, p, null);
        }

        public static void LoadProgressInvoke(Enums.LoaderPhase phase, float p, string msg)
        {
            OnLoadProgress?.Invoke(phase, p);
            OnLoadProgressDetailed?.Invoke(phase, p, msg);
        }

        public delegate void RequestLoadByQuality(Enums.TextureQuality quality);

        public static event RequestLoadByQuality OnRequestLoadByQuality;

        public static void RequestLoadByQualityInvoke(Enums.TextureQuality quality)
        {
            OnRequestLoadByQuality?.Invoke(quality);
        }

        public delegate void RequestSceneLoad(Enums.SceneVariant variant);

        public static event RequestSceneLoad OnRequestSceneLoad;

        public static void RequestLoadSceneInvoke(Enums.SceneVariant variant)
        {
            OnRequestSceneLoad?.Invoke(variant);
        }

        public static event Action OnCatalogCommitted;

        public static void CatalogCommittedInvoke()
        {
            OnCatalogCommitted?.Invoke();
        }

        public delegate void LoadProgress(Enums.LoaderPhase phase, float progress01);

        public static event Action<Enums.LoaderStatus> OnLoaderStatusChanged;

        public static void LoaderStatusChangedInvoke(Enums.LoaderStatus status)
        {
            OnLoaderStatusChanged?.Invoke(status);
        }
    }
}