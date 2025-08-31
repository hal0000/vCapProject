using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Core;
using Model;
using UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using Cysharp.Threading.Tasks;

namespace Scene
{
    public class MenuScene : BaseScene
    {
        private const long SafetyMarginBytes = 200L * 1024L * 1024L; // 200 MB
        private bool _requestInFlight;
        private SceneService _sceneService;

        public Transform ListContent;
        public AssetController AssetControllerPrefab;

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

        public override void Start()
        {
            base.Start();
            RebuildSceneAssetControllers();
        }

        public void ChangeQuality(int index)
        {
            ChangeTextureQualityAsync((Enums.TextureQuality)index, this.GetCancellationTokenOnDestroy()).Forget();
        }

        public void ChangeScene(int index)
        {
            ChangeSceneByTypeAsync((Enums.SceneVariant)index, this.GetCancellationTokenOnDestroy()).Forget();
        }

        public void FullLoadSceneByIndex(int sceneIndex)
        {
            FullLoadSceneByIndexAsync(sceneIndex, this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid ChangeTextureQualityAsync(Enums.TextureQuality type, CancellationToken token)
        {
            if (_requestInFlight) return;
            _requestInFlight = true;

            try
            {
                var variant = _sceneService.CurrentSceneVariant;
                var (ok, need) = await PreflightCoreSceneSizeAsync(variant, type, token);
                if (!ok)
                {
                    EventManager.NewNotificationInvoke(Enums.Notification.DownloadSizeQueryFailed);
                    EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Error);
                    EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
                    return;
                }

                var withMargin = need > 0 ? need + SafetyMarginBytes : 0;
                if (!HasEnoughSpace(withMargin))
                {
                    EventManager.NewNotificationInvoke(Enums.Notification.NotEnoughSpace);
                    EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.NoSpace);
                    EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
                    return;
                }

                EventManager.RequestLoadByQualityInvoke(type);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Error);
                EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
            }
            finally
            {
                _requestInFlight = false;
            }
        }

        private async UniTaskVoid ChangeSceneByTypeAsync(Enums.SceneVariant variant, CancellationToken token)
        {
            if (_requestInFlight) return;
            _requestInFlight = true;

            try
            {
                var quality = _sceneService.CurrentTextureQuality;
                var (ok, need) = await PreflightCoreSceneSizeAsync(variant, quality, token);
                if (!ok)
                {
                    EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Error);
                    EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
                    return;
                }

                var withMargin = need > 0 ? need + SafetyMarginBytes : 0;
                if (!HasEnoughSpace(withMargin))
                {
                    EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.NoSpace);
                    EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
                    return;
                }

                EventManager.RequestLoadSceneInvoke(variant);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Error);
                EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
            }
            finally
            {
                _requestInFlight = false;
            }
        }

        private async UniTaskVoid FullLoadSceneByIndexAsync(int sceneIndex, CancellationToken token)
        {
            if (_requestInFlight) return;
            _requestInFlight = true;
            try
            {
                var variant = sceneIndex == 1 ? Enums.SceneVariant.Catalog_B : Enums.SceneVariant.Catalog_A;
                _sceneService.CurrentSceneVariant = variant;
                var url = _sceneService.GetCatalogUrl(variant, _sceneService.CurrentTextureQuality);
                await _sceneService.SwitchCatalog(url, false);

                var labels = _sceneService.GetAvailableModuleLabels();
                for (var i = 0; i < labels.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var label = labels[i];
                    if (_sceneService.IsLoaded(label)) continue;
                    await _sceneService.LoadModuleAsync(label, false);
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, token);
                }

                RebuildSceneAssetControllers();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoggerExtra.LogError($"[MenuScene] FullLoadSceneByIndex failed: {ex.Message}");
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Error);
                EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
            }
            finally
            {
                _requestInFlight = false;
            }
        }

        private async UniTask<(bool ok, long bytes)> PreflightCoreSceneSizeAsync(Enums.SceneVariant variant,
            Enums.TextureQuality quality, CancellationToken token)
        {
            var url = _sceneService.GetCatalogUrl(variant, quality);
            if (string.IsNullOrEmpty(url)) return (false, 0L);
            var catH = Addressables.LoadContentCatalogAsync(url, false);
            await catH.ToUniTask(cancellationToken: token);
            if (catH.Status != AsyncOperationStatus.Succeeded || catH.Result == null)
            {
                SafeRelease(catH);
                return (false, 0L);
            }

            var locator = catH.Result;
            try
            {
                var core = variant == Enums.SceneVariant.Catalog_B ? Enums.AddressLabel.B : Enums.AddressLabel.A;

                if (!locator.Locate(core.ToString(), typeof(SceneInstance), out var locs) || locs == null ||
                    locs.Count == 0) return (false, 0L);
                var sceneLoc = ChooseSceneLocation(locs);
                var sizeH = Addressables.GetDownloadSizeAsync(sceneLoc);
                await sizeH.ToUniTask(cancellationToken: token);
                var bytes = sizeH.Status == AsyncOperationStatus.Succeeded ? sizeH.Result : 0;
                if (sizeH.IsValid()) Addressables.Release(sizeH);
                return (true, bytes);
            }
            catch
            {
                return (false, 0L);
            }
            finally
            {
                if (locator != null) Addressables.RemoveResourceLocator(locator);
                SafeRelease(catH);
            }
        }

        public void RebuildSceneAssetControllers()
        {
            for (var i = ListContent.childCount - 1; i >= 0; i--) Destroy(ListContent.GetChild(i).gameObject);
            var labels = _sceneService.GetAvailableModuleLabels();
            for (var i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
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

        private static IResourceLocation ChooseSceneLocation(IList<IResourceLocation> locs)
        {
            if (locs == null || locs.Count == 0) return null;
            if (locs.Count == 1) return locs[0];
            var best = locs[0];
            var bestKey = best.PrimaryKey ?? string.Empty;
            for (var i = 1; i < locs.Count; i++)
            {
                var k = locs[i].PrimaryKey ?? string.Empty;
                if (string.CompareOrdinal(k, bestKey) < 0)
                {
                    best = locs[i];
                    bestKey = k;
                }
            }

            return best;
        }

        private static void SafeRelease<T>(AsyncOperationHandle<T> h)
        {
            if (h.IsValid()) Addressables.Release(h);
        }

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

                LoggerExtra.LogWarning(
                    "[MenuScene] Could not resolve drive for free space check. Assuming enough space.");
                return true;
#elif UNITY_IOS
                LoggerExtra.LogWarning("[MenuScene] Free space check not implemented on this platform. Assuming enough space.");
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

        public void CleanCache()
        {
            _sceneService.ClearAllCaches();
        }

        public void UnloadAll()
        {
            _sceneService.UnloadAllAsync();
        }
    }
}