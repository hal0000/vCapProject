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
        [Header("Catalog URLs (.bin)")] public string urlQ1_512;
        public string urlQ2_1024;
        public string urlQ3_2048;

        private string _currentCatalogUrl;
        private IResourceLocator _currentLocator;
        private AsyncOperationHandle<IResourceLocator>? _currentCatalogHandle;
        private readonly Dictionary<Enums.AddressLabel, AsyncOperationHandle<SceneInstance>> _loaded = new();

        private bool _opInFlight;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            ClearAllCaches();
        }

        public async Task EnsureBootstrappedAsync()
        {
            if (_currentLocator != null) return;
            await SwitchCatalogByQuality(Enums.TextureQuality.Texture_1024, false);
        }

        public Task SwitchCatalogByQuality(Enums.TextureQuality q, bool preserveOpen = true)
        {
            return SwitchCatalog(ResolveCatalogUrl(q), preserveOpen);
        }

        public async Task SwitchCatalog(string catalogUrl, bool preserveOpen = true)
        {
            if (_opInFlight) return;
            if (string.IsNullOrEmpty(catalogUrl))
            {
                Fail("[SceneService] SwitchCatalog: URL empty.");
                return;
            }
            if (_currentLocator != null && _currentCatalogUrl == catalogUrl) return;
            _opInFlight = true;
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);
            var restoreOrder = preserveOpen ? _loaded.Keys.OrderByDescending(l => l == Enums.AddressLabel.A).ToList() : new List<Enums.AddressLabel>(0);
            var previous = preserveOpen ? new Dictionary<Enums.AddressLabel, AsyncOperationHandle<SceneInstance>>(_loaded) : null;
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
                    EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.DownloadignCatalogFailed);
                    throw new Exception("Failed to load catalog");
                }
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.DownloadingCatalogSuccessful);
                var newLocator = catH.Result;
                if (restoreOrder.Count > 0)
                {
                    await DownloadManyAndReportAsync(newLocator, restoreOrder);
                    await LoadManyAndReportAsync(newLocator, restoreOrder);
                }

                if (previous != null && previous.Count > 0)
                {
                    foreach (var kv in previous)
                    {
                        var unloadH = Addressables.UnloadSceneAsync(kv.Value, true);
                        await unloadH.Task;
                    }
                    await Resources.UnloadUnusedAssets();
                    GC.Collect();
                }

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

                _currentLocator = newLocator;
                _currentCatalogHandle = catH;
                _currentCatalogUrl = catalogUrl;
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
            }
            catch (Exception ex)
            {
                LoggerExtra.LogError($"[SceneService] SwitchCatalog failed: {ex.Message}");
                EventManager.LoaderStatusChangedInvoke(IsNet(ex) ? Enums.LoaderStatus.NoInternet : Enums.LoaderStatus.Error);
            }
            finally
            {
                _opInFlight = false;
                ReportIdle();
            }
        }

        public Task LoadCoreAsync(Enums.AddressLabel label = Enums.AddressLabel.A)
        {
            return LoadModuleAsync(label, true);
        }

        public Task LoadModuleAsync(Enums.AddressLabel label, bool makeActive = false)
        {
            return LoadByLabelInternal(label, makeActive);
        }

        public async Task UnloadModuleAsync(Enums.AddressLabel label)
        {
            if (_opInFlight) return;
            if (!_loaded.TryGetValue(label, out var h)) return;
            _opInFlight = true;
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);
            try
            {
                var unloadH = Addressables.UnloadSceneAsync(h, true);
                await unloadH.Task;
                _loaded.Remove(label);
                await Resources.UnloadUnusedAssets();
                GC.Collect();
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
            }
            catch (Exception ex)
            {
                Fail($"[SceneService] Unload '{label}' failed: {ex.Message}");
            }
            finally
            {
                _opInFlight = false;
            }
        }

        public async Task UnloadAllAsync()
        {
            if (_opInFlight) return;
            _opInFlight = true;
            try
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
            finally
            {
                _opInFlight = false;
            }
        }

        public bool ClearAllCaches()
        {
            var ok = Caching.ClearCache();
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

        private string ResolveCatalogUrl(Enums.TextureQuality q)
        {
            return q switch
            {
                Enums.TextureQuality.Texture_512 => urlQ1_512,
                Enums.TextureQuality.Texture_1024 => urlQ2_1024,
                Enums.TextureQuality.Texture_2048 => urlQ3_2048,
                _ => urlQ2_1024
            };
        }

        private async Task LoadByLabelInternal(Enums.AddressLabel label, bool makeActive)
        {
            if (_opInFlight) return;
            if (_currentLocator == null) LoggerExtra.LogWarning("[SceneService] No catalog loaded.");
            if (_loaded.ContainsKey(label)) return;
            _opInFlight = true;
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);
            try
            {
                if (!_currentLocator.Locate(label.ToString(), typeof(SceneInstance), out var locs) || locs == null || locs.Count == 0) throw new Exception($"Label '{label}' not found in catalog.");
                var sceneLoc = ChooseSceneLocation(locs);
                var bytes = await GetSizeAsync(sceneLoc);
                if (bytes > 0)
                {
                    var dl = Addressables.DownloadDependenciesAsync(new List<IResourceLocation> { sceneLoc }, false);
                    EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Downloading);
                    while (!dl.IsDone)
                    {
                        ReportDownload(dl.PercentComplete);
                        await Task.Yield();
                    }
                    if (dl.Status != AsyncOperationStatus.Succeeded) throw new Exception("Failed to download dependencies.");
                    Addressables.Release(dl);
                }
                else
                {
                    ReportDownload(1f);
                }
                var loadH = Addressables.LoadSceneAsync(sceneLoc, LoadSceneMode.Additive, true);
                while (!loadH.IsDone)
                {
                    ReportLoad(loadH.PercentComplete);
                    await Task.Yield();
                }
                if (loadH.Status != AsyncOperationStatus.Succeeded || !loadH.Result.Scene.isLoaded) throw new Exception(loadH.OperationException != null ? loadH.OperationException.Message : "Failed to load scene");
                _loaded[label] = loadH;
                if (makeActive) SceneManager.SetActiveScene(loadH.Result.Scene);
                ReportLoad(1f);
                await Resources.UnloadUnusedAssets();
                GC.Collect();
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
            }
            catch (Exception ex)
            {
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
            var locs = new List<IResourceLocation>(labels.Count);
            var sizes = new List<long>(labels.Count);
            long totalBytes = 0;
            foreach (var label in labels)
            {
                if (!locator.Locate(label.ToString(), typeof(SceneInstance), out var l) || l == null || l.Count == 0) throw new Exception($"Label '{label}' not found in catalog.");
                var loc = ChooseSceneLocation(l);
                locs.Add(loc);
                var s = await GetSizeAsync(loc);
                sizes.Add(s);
                totalBytes += s;
            }
            var accumulated = 0f;
            for (var i = 0; i < locs.Count; i++)
            {
                var bytes = sizes[i];
                if (bytes <= 0)
                {
                    if (totalBytes > 0) accumulated += (float)bytes / totalBytes;
                    ReportDownload(accumulated);
                    continue;
                }
                var dl = Addressables.DownloadDependenciesAsync(new List<IResourceLocation> { locs[i] }, false);
                while (!dl.IsDone)
                {
                    var local = dl.PercentComplete;
                    var weighted = totalBytes > 0 ? (float)bytes / totalBytes * local : 1f / labels.Count * local;
                    ReportDownload(accumulated + weighted);
                    await Task.Yield();
                }
                if (dl.Status != AsyncOperationStatus.Succeeded) throw new Exception("Failed to download dependencies.");
                Addressables.Release(dl); // MANUEL release
                accumulated += totalBytes > 0 ? (float)bytes / totalBytes : 1f / labels.Count;
                ReportDownload(accumulated);
            }
            ReportDownload(1f);
        }

        private async Task LoadManyAndReportAsync(IResourceLocator locator, List<Enums.AddressLabel> labels)
        {
            var toMakeActiveIndex = labels.FindIndex(l => l == Enums.AddressLabel.A);
            if (toMakeActiveIndex < 0) toMakeActiveIndex = 0;
            var per = labels.Count > 0 ? 1f / labels.Count : 1f;
            var baseAccum = 0f;

            for (var i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                if (!locator.Locate(label.ToString(), typeof(SceneInstance), out var l) || l == null || l.Count == 0) throw new Exception($"Label '{label}' not found in catalog.");
                var sceneLoc = ChooseSceneLocation(l);
                var loadH = Addressables.LoadSceneAsync(sceneLoc, LoadSceneMode.Additive, true);
                while (!loadH.IsDone)
                {
                    ReportLoad(baseAccum + per * loadH.PercentComplete);
                    await Task.Yield();
                }
                if (loadH.Status != AsyncOperationStatus.Succeeded || !loadH.Result.Scene.isLoaded) throw new Exception(loadH.OperationException != null ? loadH.OperationException.Message : "Failed to load scene");
                _loaded[label] = loadH;
                if (label == Enums.AddressLabel.A) SceneManager.SetActiveScene(loadH.Result.Scene);
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

        private void ReportDownload(float normalized01)
        {
            var p = Mathf.Clamp01(normalized01) * 0.5f;
            EventManager.LoadProgressInvoke(Enums.LoaderPhase.DownloadingDependencies, p);
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
        }

        private void OnDisable()
        {
            EventManager.OnRequestLoadByQuality -= HandleRequestByQuality;
        }

        private async void HandleRequestByQuality(Enums.TextureQuality quality, string _)
        {
            await SwitchCatalogByQuality(quality, true);
            if (!IsLoaded(Enums.AddressLabel.A)) await LoadCoreAsync();
        }
    }
}