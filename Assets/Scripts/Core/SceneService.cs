using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Core
{
    [DefaultExecutionOrder(-1000)]
    public class SceneService : MonoBehaviour
    {
        private string _currentCatalogUrl;
        private IResourceLocator _currentLocator;
        private AsyncOperationHandle<IResourceLocator>? _currentCatalogHandle;
        private readonly Dictionary<Enums.AddressLabel, AsyncOperationHandle<SceneInstance>> _loaded = new();

        private bool _opInFlight;
        private const string HostBase = "http://127.0.0.1:44563";
        private const string Platform = "StandaloneWindows64";
        public Enums.SceneVariant CurrentSceneVariant = Enums.SceneVariant.Catalog_A;
        public Enums.TextureQuality CurrentTextureQuality = Enums.TextureQuality.Texture_1024;
        private bool _initCalled = false;
        public string GetCatalogUrl(Enums.SceneVariant variant, Enums.TextureQuality quality)
        {
            var q = quality == Enums.TextureQuality.Texture_512 ? "512" :
                quality == Enums.TextureQuality.Texture_1024 ? "1024" : "2048";
            var c = variant == Enums.SceneVariant.Catalog_A ? "A" : "B";
            return $"{HostBase}/{Platform}/{q}/catalog_{c}_{q}.bin";
        }

        public async Task SwitchCatalog(string catalogUrl, bool preserveOpen = true)
        {
            if (_opInFlight) { EventManager.NewNotificationInvoke(Enums.Notification.Busy); return; }
            if (string.IsNullOrEmpty(catalogUrl))
            {
                EventManager.NewNotificationInvoke(Enums.Notification.InvalidCatalogUrl);
                Fail("[SceneService] SwitchCatalog: URL empty.");
                return;
            }

            if (!string.IsNullOrEmpty(_currentCatalogUrl) &&
                string.Equals(_currentCatalogUrl, catalogUrl, StringComparison.Ordinal))
            {
                EventManager.NewNotificationInvoke(Enums.Notification.NoChange);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
                return;
            }

            _opInFlight = true;
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);

            var core = CurrentSceneVariant == Enums.SceneVariant.Catalog_B ? Enums.AddressLabel.B : Enums.AddressLabel.A;
            var restoreOrder = (preserveOpen && _loaded.Count > 0)
                ? _loaded.Keys.OrderByDescending(l => l == core).ToList()
                : new List<Enums.AddressLabel> { core };

            try
            {
                ReportDownload(0f);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.AttemptingToDownloadCatalog);

                var initH = Addressables.InitializeAsync();
                await initH.Task;

                var catH = Addressables.LoadContentCatalogAsync(catalogUrl, false);
                await catH.Task;
                if (catH.Status != AsyncOperationStatus.Succeeded || catH.Result == null)
                {
                    EventManager.NewNotificationInvoke(Enums.Notification.CatalogInitFailed);
                    EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.DownloadignCatalogFailed);
                    throw new Exception("Failed to load catalog");
                }

                EventManager.NewNotificationInvoke(Enums.Notification.CatalogSwitchSuccess);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.DownloadingCatalogSuccessful);

                var newLocator = catH.Result;

                await DownloadManyAndReportAsync(newLocator, restoreOrder);

                if (_loaded.Count > 0)
                {
                    foreach (var kv in _loaded)
                    {
                        var unloadH = Addressables.UnloadSceneAsync(kv.Value, true);
                        await unloadH.Task;
                    }
                    _loaded.Clear();
                    await Resources.UnloadUnusedAssets();
                    GC.Collect();
                }

                if (_currentLocator != null) { Addressables.RemoveResourceLocator(_currentLocator); _currentLocator = null; }
                if (_currentCatalogHandle.HasValue) { SafeRelease(_currentCatalogHandle.Value); _currentCatalogHandle = null; }

                _currentLocator = newLocator;
                _currentCatalogHandle = catH;
                _currentCatalogUrl = catalogUrl;

                await LoadManyAndReportAsync(_currentLocator, restoreOrder);

                EventManager.CatalogCommittedInvoke();
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
            }
            catch (Exception ex)
            {
                bool isNet = IsNet(ex);
                EventManager.NewNotificationInvoke(isNet ? Enums.Notification.NoInternet : Enums.Notification.CatalogSwitchFailed);
                LoggerExtra.LogError($"[SceneService] SwitchCatalog failed: {ex.Message}");
                EventManager.LoaderStatusChangedInvoke(isNet ? Enums.LoaderStatus.NoInternet : Enums.LoaderStatus.Error);
            }
            finally
            {
                _opInFlight = false;
                ReportIdle();
            }
        }


        public Task LoadModuleAsync(Enums.AddressLabel label, bool makeActive = false)
        {
            return LoadByLabelInternal(label, makeActive);
        }

        public async Task UnloadModuleAsync(Enums.AddressLabel label)
        {
            if (_opInFlight) { EventManager.NewNotificationInvoke(Enums.Notification.Busy); return; }
            if (!_loaded.TryGetValue(label, out var h))
            {
                EventManager.NewNotificationInvoke(Enums.Notification.ModuleNotLoaded);
                return;
            }

            _opInFlight = true;
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);
            try
            {
                var unloadH = Addressables.UnloadSceneAsync(h, true);
                await unloadH.Task;

                _loaded.Remove(label);
                await Resources.UnloadUnusedAssets();
                GC.Collect();

                EventManager.NewNotificationInvoke(Enums.Notification.ModuleUnloaded);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
            }
            catch (Exception ex)
            {
                EventManager.NewNotificationInvoke(Enums.Notification.SceneUnloadFailed);
                Fail($"[SceneService] Unload '{label}' failed: {ex.Message}");
            }
            finally
            {
                _opInFlight = false;
                ReportIdle();
            }
        }
        
        public async Task UnloadAllAsync()
        {
            if (_opInFlight) return;
            _opInFlight = true;
            try
            {
                await UnloadOpenScenesInternalAsync();
            }
            finally
            {
                _opInFlight = false;
            }
        }

        private async Task UnloadOpenScenesInternalAsync()
        {
            if (_loaded.Count == 0) return;

            foreach (var kv in _loaded)
            {
                var unloadH = Addressables.UnloadSceneAsync(kv.Value, true);
                await unloadH.Task;
            }

            _loaded.Clear();
            await Resources.UnloadUnusedAssets();
            GC.Collect();
        }

        public bool ClearAllCaches()
        {
            var ok = Caching.ClearCache();
            if (!ok) EventManager.NewNotificationInvoke(Enums.Notification.CacheClearFailed);
            LoggerExtra.Log($"[SceneService] Caching.ClearCache() => {ok}");
            if (_currentLocator != null)
            {
                Addressables.RemoveResourceLocator(_currentLocator);
                _currentLocator = null;
            }

            if (_currentCatalogHandle.HasValue)
            {
                SafeRelease(_currentCatalogHandle.Value);
                _currentCatalogHandle = null;
            }

            _currentCatalogUrl = null;
            return ok;
        }

        public bool IsLoaded(Enums.AddressLabel label)
        {
            return _loaded.ContainsKey(label);
        }

        public IReadOnlyList<Enums.AddressLabel> GetOpenLabels()
        {
            return _loaded.Keys.ToList();
        }

        private async Task LoadByLabelInternal(Enums.AddressLabel label, bool makeActive)
        {
            if (_opInFlight) { EventManager.NewNotificationInvoke(Enums.Notification.Busy); return; }

            if (_currentLocator == null)
            {
                EventManager.NewNotificationInvoke(Enums.Notification.LabelNotFound);
                LoggerExtra.LogWarning("[SceneService] No catalog loaded.");
                return;
            }

            if (_loaded.ContainsKey(label))
            {
                EventManager.NewNotificationInvoke(Enums.Notification.ModuleAlreadyLoaded);
                return;
            }

            _opInFlight = true;
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);
            try
            {
                if (!_currentLocator.Locate(label.ToString(), typeof(SceneInstance), out var locs) || locs == null || locs.Count == 0)
                {
                    EventManager.NewNotificationInvoke(Enums.Notification.LabelNotFound);
                    throw new Exception($"Label '{label}' not found in catalog.");
                }

                var sceneLoc = ChooseSceneLocation(locs);
                var bytes = await GetSizeAsync(sceneLoc);

                if (bytes > 0)
                {
                    var dl = Addressables.DownloadDependenciesAsync(new List<IResourceLocation> { sceneLoc }, false);
                    EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Downloading);

                    while (!dl.IsDone)
                    {
                        ReportDownload(dl.PercentComplete, $"{FormatBytes((long)(bytes * dl.PercentComplete))} / {FormatBytes(bytes)}");
                        await Task.Yield();
                    }

                    if (dl.Status != AsyncOperationStatus.Succeeded)
                    {
                        EventManager.NewNotificationInvoke(Enums.Notification.DependenciesDownloadFailed);
                        throw new Exception("Failed to download dependencies.");
                    }

                    Addressables.Release(dl);
                }
                else
                {
                    ReportDownload(1f, "0 B / 0 B");
                }

                var loadH = Addressables.LoadSceneAsync(sceneLoc, LoadSceneMode.Additive, true);
                while (!loadH.IsDone)
                {
                    ReportLoad(loadH.PercentComplete);
                    await Task.Yield();
                }

                if (loadH.Status != AsyncOperationStatus.Succeeded || !loadH.Result.Scene.isLoaded)
                {
                    EventManager.NewNotificationInvoke(Enums.Notification.SceneLoadFailed);
                    throw new Exception(loadH.OperationException != null ? loadH.OperationException.Message : "Failed to load scene");
                }

                _loaded[label] = loadH;
                if (makeActive) SceneManager.SetActiveScene(loadH.Result.Scene);

                ReportLoad(1f);
                await Resources.UnloadUnusedAssets();
                GC.Collect();

                EventManager.NewNotificationInvoke(Enums.Notification.ModuleLoaded);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
            }
            catch (Exception ex)
            {
                EventManager.NewNotificationInvoke(IsNet(ex) ? Enums.Notification.NoInternet : Enums.Notification.SceneLoadFailed);
                Fail($"[SceneService] Load '{label}' failed: {ex.Message}");
            }
            finally
            {
                _opInFlight = false;
                ReportIdle();
            }
        }


        private async Task DownloadManyAndReportAsync(IResourceLocator locator, List<Enums.AddressLabel> labels)
        {
            var core = GetCoreLabel();
            var locs = new List<IResourceLocation>(labels.Count);
            var sizes = new List<long>(labels.Count);
            long totalBytes = 0;

            foreach (var label in labels)
            {
                if (!locator.Locate(label.ToString(), typeof(SceneInstance), out var l) || l == null || l.Count == 0)
                {
                    if (label == core) throw new Exception($"Core label '{label}' not found in catalog.");
                    LoggerExtra.LogWarning($"[SceneService] Optional label '{label}' not found. Skipping.");
                    continue;
                }

                var loc = ChooseSceneLocation(l);
                locs.Add(loc);
                var s = await GetSizeAsync(loc);
                sizes.Add(s);
                totalBytes += s;
            }

            long accumulatedBytes = 0;
            var accumulated = 0f;
            for (var i = 0; i < locs.Count; i++)
            {
                var bytes = sizes[i];
                if (bytes <= 0)
                {
                    var msg0 = totalBytes > 0 ? $"{FormatBytes(accumulatedBytes)} / {FormatBytes(totalBytes)}" : null;
                    if (totalBytes > 0) accumulated += (float)bytes / totalBytes;
                    ReportDownload(accumulated, msg0);
                    continue;
                }

                var dl = Addressables.DownloadDependenciesAsync(new List<IResourceLocation> { locs[i] }, false);
                while (!dl.IsDone)
                {
                    var local = dl.PercentComplete;
                    var weighted = totalBytes > 0
                        ? (float)bytes / totalBytes * local
                        : 1f / Math.Max(1, locs.Count) * local;
                    var overall = accumulated + weighted;
                    var done = accumulatedBytes + (long)(bytes * local);
                    var msg = totalBytes > 0 ? $"{FormatBytes(done)} / {FormatBytes(totalBytes)}" : null;
                    ReportDownload(overall, msg);
                    await Task.Yield();
                }

                if (dl.Status != AsyncOperationStatus.Succeeded)
                    throw new Exception("Failed to download dependencies.");
                Addressables.Release(dl);

                accumulatedBytes += bytes;
                accumulated += totalBytes > 0 ? (float)bytes / totalBytes : 1f / Math.Max(1, locs.Count);

                var msgAfter = totalBytes > 0 ? $"{FormatBytes(accumulatedBytes)} / {FormatBytes(totalBytes)}" : null;
                ReportDownload(accumulated, msgAfter);
            }

            var msgDone = totalBytes > 0 ? $"{FormatBytes(totalBytes)} / {FormatBytes(totalBytes)}" : null;
            ReportDownload(1f, msgDone);
        }

        private async Task LoadManyAndReportAsync(IResourceLocator locator, List<Enums.AddressLabel> labels)
        {
            var core = CurrentSceneVariant == Enums.SceneVariant.Catalog_B
                ? Enums.AddressLabel.B
                : Enums.AddressLabel.A;

            var present = new List<Enums.AddressLabel>();
            foreach (var label in labels)
                if (locator.Locate(label.ToString(), typeof(SceneInstance), out var l) && l != null && l.Count > 0)
                    present.Add(label);
                else if (label == core)
                    throw new Exception($"Core label '{label}' not found in catalog.");
                else
                    LoggerExtra.LogWarning($"[SceneService] Optional label '{label}' not found. Skipping.");

            var per = present.Count > 0 ? 1f / present.Count : 1f;
            var baseAccum = 0f;

            for (var i = 0; i < present.Count; i++)
            {
                var label = present[i];
                locator.Locate(label.ToString(), typeof(SceneInstance), out var l);
                var sceneLoc = ChooseSceneLocation(l);
                var loadH = Addressables.LoadSceneAsync(sceneLoc, LoadSceneMode.Additive, true);

                while (!loadH.IsDone)
                {
                    ReportLoad(baseAccum + per * loadH.PercentComplete);
                    await Task.Yield();
                }

                if (loadH.Status != AsyncOperationStatus.Succeeded || !loadH.Result.Scene.isLoaded)
                    throw new Exception(loadH.OperationException != null
                        ? loadH.OperationException.Message
                        : "Failed to load scene");
                _loaded[label] = loadH;
                if (label == core) SceneManager.SetActiveScene(loadH.Result.Scene);
                baseAccum += per;
                ReportLoad(baseAccum);
            }

            ReportLoad(1f);
        }

        private static async Task<long> GetSizeAsync(IResourceLocation loc)
        {
            var sizeH = Addressables.GetDownloadSizeAsync(loc);
            await sizeH.Task;
            var bytes = sizeH.Status == AsyncOperationStatus.Succeeded ? sizeH.Result : 0;
            if (sizeH.IsValid()) Addressables.Release(sizeH);
            return bytes;
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

        private static void SafeRelease<T>(AsyncOperationHandle<T> h)
        {
            if (h.IsValid()) Addressables.Release(h);
        }

        private static bool IsNet(Exception ex)
        {
            var m = ex.Message.ToLowerInvariant();
            return m.Contains("connect") || m.Contains("resolve") || m.Contains("timed out");
        }

        private void Fail(string msg)
        {
            LoggerExtra.LogError(msg);
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Error);
            ReportIdle();
        }

        private void ReportDownload(float normalized01, string message = null)
        {
            var p = Mathf.Clamp01(normalized01) * 0.5f;
            EventManager.LoadProgressInvoke(Enums.LoaderPhase.DownloadingDependencies, p, message);
        }

        private void ReportLoad(float normalized01)
        {
            var p = 0.5f + Mathf.Clamp01(normalized01) * 0.5f;
            EventManager.LoadProgressInvoke(Enums.LoaderPhase.SceneLoading, p);
        }

        private void ReportIdle()
        {
            EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
        }

        private void OnEnable()
        {
            EventManager.OnRequestLoadByQuality += HandleRequestByQuality;
            EventManager.OnRequestSceneLoad += HandleRequestByScene;
        }

        private void OnDisable()
        {
            EventManager.OnRequestLoadByQuality -= HandleRequestByQuality;
            EventManager.OnRequestSceneLoad -= HandleRequestByScene;
        }

        private async void HandleRequestByQuality(Enums.TextureQuality quality)
        {
            if (quality == CurrentTextureQuality)
            {
                EventManager.NewNotificationInvoke(Enums.Notification.NoChange);
                return;
            }
            CurrentTextureQuality = quality;
            await SwitchCatalog(GetCatalogUrl(CurrentSceneVariant, CurrentTextureQuality), true);
        }

        private async void HandleRequestByScene(Enums.SceneVariant variant)
        {
            if (_initCalled && variant == CurrentSceneVariant)
            {
                EventManager.NewNotificationInvoke(Enums.Notification.NoChange);
                return;
            }
            _initCalled = true;
            CurrentSceneVariant = variant;
            await SwitchCatalog(GetCatalogUrl(CurrentSceneVariant, CurrentTextureQuality), false);
        }

        public Enums.AddressLabel GetCoreLabel()
        {
            return CurrentSceneVariant == Enums.SceneVariant.Catalog_B ? Enums.AddressLabel.B : Enums.AddressLabel.A;
        }

        public IReadOnlyList<Enums.AddressLabel> GetAvailableModuleLabels()
        {
            var result = new List<Enums.AddressLabel>();
            if (_currentLocator == null) return result;

            foreach (Enums.AddressLabel label in Enum.GetValues(typeof(Enums.AddressLabel)))
            {
                if (label == Enums.AddressLabel.A || label == Enums.AddressLabel.B) continue;
                if (_currentLocator.Locate(label.ToString(), typeof(SceneInstance), out var locs) && locs != null &&
                    locs.Count > 0)
                    result.Add(label);
            }

            return result;
        }

        private static string FormatBytes(long bytes)
        {
            const double KB = 1024.0, MB = 1024.0 * 1024.0, GB = 1024.0 * 1024.0 * 1024.0;
            if (bytes >= GB) return (bytes / GB).ToString("0.00") + " GB";
            if (bytes >= MB) return (bytes / MB).ToString("0.00") + " MB";
            if (bytes >= KB) return (bytes / KB).ToString("0") + " KB";
            return bytes + " B";
        }
    }
}