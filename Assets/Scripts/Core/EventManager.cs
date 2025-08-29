using System;

namespace Core
{
    /// <summary>
    ///     Minimal event hub for the loader flow.
    /// </summary>
    public static class EventManager
    {
        public delegate void RequestLoadByQuality(Enums.TextureQuality quality, string sceneLabel);

        public static event RequestLoadByQuality OnRequestLoadByQuality;

        public static void RequestLoadByQualityInvoke(Enums.TextureQuality quality, string sceneLabel)
        {
            OnRequestLoadByQuality?.Invoke(quality, sceneLabel);
        }

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