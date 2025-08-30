namespace Core
{
    public static class Enums
    {
        public enum TextureQuality
        {
            Texture_512 = 0,
            Texture_1024 = 1,
            Texture_2048 = 2
        }

        public enum SceneVariant
        {
            Catalog_A = 0,
            Catalog_B = 1
        }

        public enum LoaderPhase
        {
            Idle,
            CatalogLoading,
            CatalogUnLoading,
            LocatingScenes,
            DownloadingDependencies,
            SceneLoading,
            Finalizing
        }

        public enum LoaderStatus
        {
            None,
            Starting,
            Connecting,
            Downloading,
            Retrying,
            Completed,
            Error,
            NoInternet,
            NoSpace,
            Paused,
            AttemptingToDownloadCatalog,
            DownloadignCatalogFailed,
            DownloadingCatalogSuccessful
        }

        public enum AddressLabel
        {
            A, // "A" (base scene)
            B, // "B" (base scene)
            Curtain,
            Furniture,
            Props,
            Reflection
        }

        public enum Notification
        {
            None = 0,

            // --- Loader / Addressables: Errors ---
            InvalidCatalogUrl,
            CatalogInitFailed,
            CatalogDownloadFailed,
            CatalogSwitchFailed,
            NoInternet,
            LabelNotFound,
            DownloadSizeQueryFailed,
            DependenciesDownloadFailed,
            SceneLoadFailed,
            SceneUnloadFailed,
            NotEnoughSpace,
            CacheClearFailed,

            // --- Success / Info ---
            CatalogSwitchSuccess,
            ModuleLoaded,
            ModuleUnloaded,
            AlreadyCurrentQuality,
            AlreadyCurrentScene,
            NoChange,
            ModuleAlreadyLoaded,
            ModuleNotLoaded,
            Busy

        }
    }
}