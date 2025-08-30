using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Core;
using Model;
using UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Scene
{
    /// <summary>
    ///     Scene-level controller that raises quality change requests via events
    ///     and performs the actual Addressables scene switch (Single).
    ///     Includes test helpers to force re-download by clearing dependency cache.
    /// </summary>
    public class MenuScene : BaseScene
    {
        // Download state (from events)
        private bool _isDownloading;

        // Optional safety margin for free-space checks (in bytes)
        private const long SafetyMarginBytes = 200L * 1024L * 1024L; // 200 MB

        private bool _isSwitching;
        private string _currentUrl;
        private IResourceLocator _currentLocator;
        private AsyncOperationHandle<IResourceLocator>? _currentCatalogHandle;
        private SceneService _sceneService;
        public Transform ListContent;

        public override void Awake()
        {
            base.Awake();
            _gm.CurrentScene = this;
            _sceneService = _gm.SceneService;
            EventManager.OnCatalogCommitted += RebuildSceneAssetControllers;
        }
        public override void OnDestroy()
        {
            base.OnDestroy();
            EventManager.OnCatalogCommitted -= RebuildSceneAssetControllers;
        }
        public void ChangeQuality(int index)
        {
            ChangeTextureQuality((Enums.TextureQuality)index);
        }
        public void ChangeScene(int index)
        {
            ChangeSceneByType((Enums.SceneVariant)index);
        }
        private bool _requestInFlight;

        /// <summary>
        ///     Entry point from UI: user selected a new texture quality.
        ///     Does: size check -> free space -> no concurrent download -> actual switch here.
        /// </summary>
        public async void ChangeTextureQuality(Enums.TextureQuality type)
        {
            if (_requestInFlight) return;
            _requestInFlight = true;
            
            // 1) Preflight: estimate size (optional but useful for NoSpace)
            var api = await RequestQualityChangeAsync(type);
            if (!api.success)
            {
                EventManager.NewNotificationInvoke(Enums.Notification.DownloadSizeQueryFailed);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Error);
                EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
                _requestInFlight = false;
                return;
            }

            // 2) Free space check (+ margin)
            var requiredWithMargin = api.requiredBytes > 0 ? api.requiredBytes + SafetyMarginBytes : 0;
            if (!HasEnoughSpace(requiredWithMargin))
            {
                EventManager.NewNotificationInvoke(Enums.Notification.NotEnoughSpace);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.NoSpace);
                EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
                _requestInFlight = false;
                return;
            }
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);
            EventManager.RequestLoadByQualityInvoke(type);

            _requestInFlight = false;
        }
        public async void ChangeSceneByType(Enums.SceneVariant variant)
        {
            if (_requestInFlight) return;
            _requestInFlight = true;
            
            // 1) Preflight: estimate size (optional but useful for NoSpace)
            var api = await RequestSceneChangeAsync(variant);
            if (!api.success)
            {
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Error);
                EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
                _requestInFlight = false;
                return;
            }

            // 2) Free space check (+ margin)
            var requiredWithMargin = api.requiredBytes > 0 ? api.requiredBytes + SafetyMarginBytes : 0;
            if (!HasEnoughSpace(requiredWithMargin))
            {
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.NoSpace);
                EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
                _requestInFlight = false;
                return;
            }
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);
            EventManager.RequestLoadSceneInvoke(variant);
            _requestInFlight = false;
        }
        public override void Start()
        {
            base.Start();
            RebuildSceneAssetControllers();
        }

        /// <summary>
        ///     Dry-run: load target catalog, locate the scene, (optional) clear dependency cache for test,
        ///     ask Addressables for download size. Clean up everything.
        /// </summary>
        private async Task<(bool success, long requiredBytes, int errorCode)> RequestQualityChangeAsync(Enums.TextureQuality type)
        {
            var catalogUrl = _sceneService.GetCatalogUrl(_sceneService.CurrentSceneVariant, type);
            if (string.IsNullOrEmpty(catalogUrl)) return (false, 0L, 400);
            var catH = Addressables.LoadContentCatalogAsync(catalogUrl, false);
            await catH.Task;
            if (catH.Status != AsyncOperationStatus.Succeeded || catH.Result == null)
            {
                SafeRelease(catH);
                return (false, 0L, 503);
            }
            var locator = catH.Result;
            try
            {
                var core = (_sceneService.CurrentSceneVariant == Enums.SceneVariant.Catalog_B) ? Enums.AddressLabel.B : Enums.AddressLabel.A;
                if (!locator.Locate(core.ToString(), typeof(SceneInstance), out var locs) || locs == null || locs.Count == 0)
                    return (false, 0L, 404);
                var sceneLoc = ChooseSceneLocation(locs);
                var sceneLocList = new List<IResourceLocation> { sceneLoc };
                var sizeH = Addressables.GetDownloadSizeAsync(sceneLocList);
                await sizeH.Task;
                var bytes = sizeH.Status == AsyncOperationStatus.Succeeded ? sizeH.Result : 0;
                SafeRelease(sizeH);
                return (true, bytes, 0);
            }
            catch { return (false, 0L, 500); }
            finally
            {
                if (locator != null) Addressables.RemoveResourceLocator(locator);
                SafeRelease(catH);
            }
        }
        /// <summary>
        ///     Dry-run: load target catalog, locate the scene, (optional) clear dependency cache for test,
        ///     ask Addressables for download size. Clean up everything.
        /// </summary>
        private async Task<(bool success, long requiredBytes, int errorCode)> RequestSceneChangeAsync(Enums.SceneVariant variant)
        {
            var catalogUrl = _sceneService.GetCatalogUrl(variant, _sceneService.CurrentTextureQuality);
            if (string.IsNullOrEmpty(catalogUrl)) return (false, 0L, 400);
            var catH = Addressables.LoadContentCatalogAsync(catalogUrl, false);
            await catH.Task;
            if (catH.Status != AsyncOperationStatus.Succeeded || catH.Result == null)
            {
                SafeRelease(catH);
                return (false, 0L, 503);
            }
            var locator = catH.Result;
            try
            {
                var core = (variant == Enums.SceneVariant.Catalog_B) ? Enums.AddressLabel.B : Enums.AddressLabel.A;
                if (!locator.Locate(core.ToString(), typeof(SceneInstance), out var locs) || locs == null || locs.Count == 0)
                    return (false, 0L, 404);
                var sceneLoc = ChooseSceneLocation(locs);
                var sceneLocList = new List<IResourceLocation> { sceneLoc };
                var sizeH = Addressables.GetDownloadSizeAsync(sceneLocList);
                await sizeH.Task;
                var bytes = sizeH.Status == AsyncOperationStatus.Succeeded ? sizeH.Result : 0;
                SafeRelease(sizeH);
                return (true, bytes, 0);
            }
            catch { return (false, 0L, 500); }
            finally
            {
                if (locator != null) Addressables.RemoveResourceLocator(locator);
                SafeRelease(catH);
            }
        }
        private static IResourceLocation ChooseSceneLocation(IList<IResourceLocation> locs)
        {
            if (locs.Count == 1) return locs[0];
            var best = locs[0];
            var bestKey = best.PrimaryKey ?? string.Empty;
            for (var i = 1; i < locs.Count; i++)
            {
                var k = locs[i].PrimaryKey ?? string.Empty;
                if (string.CompareOrdinal(k, bestKey) >= 0) continue;
                best = locs[i];
                bestKey = k;
            }
            return best;
        }

        public AssetController AssetControllerPrefab;

        public void RebuildSceneAssetControllers()
        {
            for (int i = ListContent.childCount - 1; i >= 0; i--)
                Destroy(ListContent.GetChild(i).gameObject);

            var labels = _sceneService.GetAvailableModuleLabels();
            foreach (var label in labels)
            {
                var go = Instantiate(AssetControllerPrefab, ListContent);
                var model = new AssetModel
                {
                    Name = label.ToString(),
                    IsActive = _sceneService.IsLoaded(label),
                    Label = label
                };
                go.Init(model);

                go.ButtonEnable.ClickAction.AddListener(async () =>
                {
                    await _sceneService.LoadModuleAsync(label, true);
                    model.IsActive = _sceneService.IsLoaded(label);
                    go.SendMessage("SetData", SendMessageOptions.DontRequireReceiver);
                });

                go.ButtonDisable.ClickAction.AddListener(async () =>
                {
                    await _sceneService.UnloadModuleAsync(label);
                    model.IsActive = _sceneService.IsLoaded(label);
                    go.SendMessage("SetData", SendMessageOptions.DontRequireReceiver);
                });

                go.gameObject.SetActive(true);
            }
        }


        private static void SafeRelease<T>(AsyncOperationHandle<T> h)
        {
            if (h.IsValid()) Addressables.Release(h);
        }

        /// <summary>
        ///     True if the device has at least 'bytesNeeded' free in the storage backing persistentDataPath.
        ///     Android: StatFs. Desktop/Editor: DriveInfo. Others: conservative fallback.
        /// </summary>
        private bool HasEnoughSpace(long bytesNeeded)
        {
            if (bytesNeeded <= 0) return true;

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var file = new AndroidJavaObject("java.io.File", Application.persistentDataPath))
                using (var statFs = new AndroidJavaObject("android.os.StatFs", file.Call<string>("getAbsolutePath"))) {
                    long availBlocks, blockSize;
                    using (var build = new AndroidJavaClass("android.os.Build$VERSION")) {
                        int sdk = build.GetStatic<int>("SDK_INT");
                        if (sdk >= 18) { availBlocks = statFs.Call<long>("getAvailableBlocksLong"); blockSize =
 statFs.Call<long>("getBlockSizeLong"); }
                        else { availBlocks = statFs.Call<int>("getAvailableBlocks"); blockSize =
 statFs.Call<int>("getBlockSize"); }
                    }
                    long freeBytes = availBlocks * blockSize;
                    return freeBytes >= bytesNeeded;
                }
#elif (UNITY_STANDALONE || UNITY_EDITOR)
                var path = Application.persistentDataPath;
                var root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root)) root = Path.GetPathRoot(Directory.GetCurrentDirectory());
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    var freeBytes = drive.AvailableFreeSpace;
                    return freeBytes >= bytesNeeded;
                }

                LoggerExtra.LogWarning("[MenuScene] Could not resolve drive for free space check. Assuming enough space.");
                return true;
#elif UNITY_IOS
                LoggerExtra.LogWarning("[MenuScene] iOS free space check requires a native plugin. Assuming enough space for now.");
                return true;
#else
                LoggerExtra.LogWarning("[MenuScene] Free space check not implemented on this platform. Assuming enough space.");
                return true;
#endif
            }
            catch (Exception ex)
            {
                LoggerExtra.LogWarning($"[MenuScene] Free space check failed: {ex.Message}. Assuming enough space.");
                return true;
            }
        }
    }
}