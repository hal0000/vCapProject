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
        public static void CatalogCommittedInvoke() => OnCatalogCommitted?.Invoke();

        public delegate void LoadProgress(Enums.LoaderPhase phase, float progress01);

        public static event LoadProgress OnLoadProgress;

        public static void LoadProgressInvoke(Enums.LoaderPhase phase, float progress01)
        {
            OnLoadProgress?.Invoke(phase, progress01);
        }

        public static event Action<Enums.LoaderStatus> OnLoaderStatusChanged;

        public static void LoaderStatusChangedInvoke(Enums.LoaderStatus status)
        {
            OnLoaderStatusChanged?.Invoke(status);
        }
    }
}