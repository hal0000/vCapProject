using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks; // UniTask

namespace Core
{
    [DefaultExecutionOrder(-1000)]
    public class SceneService : MonoBehaviour
    {
        private const bool RunGcAfterHeavyOps = true;
        private const float ProgressThrottleSec = 0.12f;
        private const long  BytesStepForText   = 1 << 20; //every one mb
        private string _currentCatalogUrl;
        private IResourceLocator _currentLocator;
        private AsyncOperationHandle<IResourceLocator>? _currentCatalogHandle;
        private readonly Dictionary<Enums.AddressLabel, AsyncOperationHandle<SceneInstance>> _loaded = new(8);
        private bool _opInFlight;
        private bool _initCalled;

        private CancellationTokenSource _opCts;

        private readonly List<IResourceLocation> _tmpLocs  = new(8);
        private readonly List<long>              _tmpSizes = new(8);
        private readonly List<Enums.AddressLabel> _tmpLabels = new(8);

        private float _lastProgTs;
        private long  _lastBytesShown;

        public Enums.SceneVariant  CurrentSceneVariant  = Enums.SceneVariant.Catalog_A;
        public Enums.TextureQuality CurrentTextureQuality = Enums.TextureQuality.Texture_1024;

        private const string GH_REPO = "hal0000/vCapProject";
        private static string SizeStr(Enums.TextureQuality q) => q == Enums.TextureQuality.Texture_512  ? "512" : q == Enums.TextureQuality.Texture_1024 ? "1024" : "2048";
        private static string CoreStr(Enums.SceneVariant v) => v == Enums.SceneVariant.Catalog_A ? "A" : "B";

        private static string TagFor(Enums.SceneVariant v, Enums.TextureQuality q)
            => $"{CoreStr(v)}_{SizeStr(q)}";

        private static string GithubReleaseBase(Enums.SceneVariant v, Enums.TextureQuality q)
            => $"https://github.com/{GH_REPO}/releases/download/{TagFor(v, q)}";

        public string GetCatalogUrl(Enums.SceneVariant variant, Enums.TextureQuality quality)
        {
            var c = CoreStr(variant);
            var qStr = SizeStr(quality);
            return $"{GithubReleaseBase(variant, quality)}/catalog_{c}_{qStr}.bin";
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
            if (!string.IsNullOrEmpty(_currentCatalogUrl) && string.Equals(_currentCatalogUrl, catalogUrl, StringComparison.Ordinal))
            {
                EventManager.NewNotificationInvoke(Enums.Notification.NoChange);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
                return;
            }

            _opInFlight = true;
            var token = BeginOp();
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);
            ReportDownloadInternal(0f, null);

            try
            {
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.AttemptingToDownloadCatalog);
                await Addressables.InitializeAsync().ToUniTask(cancellationToken: token);

                var catH = Addressables.LoadContentCatalogAsync(catalogUrl, autoReleaseHandle: false);
                await catH.ToUniTask(cancellationToken: token);
                if (catH.Status != AsyncOperationStatus.Succeeded || catH.Result == null)
                {
                    EventManager.NewNotificationInvoke(Enums.Notification.CatalogInitFailed);
                    EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.DownloadignCatalogFailed);
                    throw new Exception("Failed to load catalog");
                }

                EventManager.NewNotificationInvoke(Enums.Notification.CatalogSwitchSuccess);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.DownloadingCatalogSuccessful);

                var newLocator = catH.Result;
                _tmpLabels.Clear();
                var core = GetCoreLabel();
                _tmpLabels.Add(core);
                if (preserveOpen && _loaded.Count > 0)
                {
                    foreach (var kv in _loaded)
                    {
                        var label = kv.Key;
                        if (label != core) _tmpLabels.Add(label);
                    }
                }
                var plan = new List<Enums.AddressLabel>(_tmpLabels);
                await DownloadManyAndReportAsync(newLocator, plan, token);

                if (_loaded.Count > 0)
                {
                    await UnloadOpenScenesInternalAsync(token);
                }
                // Locator swap
                if (_currentLocator != null) { Addressables.RemoveResourceLocator(_currentLocator); _currentLocator = null; }
                if (_currentCatalogHandle.HasValue) { SafeRelease(_currentCatalogHandle.Value); _currentCatalogHandle = null; }

                _currentLocator       = newLocator;
                _currentCatalogHandle = catH;
                _currentCatalogUrl    = catalogUrl;

                await LoadManyAndReportAsync(_currentLocator, plan, token);

                await Resources.UnloadUnusedAssets();
                if (RunGcAfterHeavyOps) GC.Collect();
                EventManager.CatalogCommittedInvoke();
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
            }
            catch (OperationCanceledException)
            {
                //nothing
            }
            catch (Exception ex)
            {
                var isNet = IsNet(ex);
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
        public Task LoadModuleAsync(Enums.AddressLabel label, bool makeActive = false) => LoadByLabelInternal(label, makeActive);

        public async Task UnloadModuleAsync(Enums.AddressLabel label)
        {
            if (_opInFlight) { EventManager.NewNotificationInvoke(Enums.Notification.Busy); return; }
            if (!_loaded.TryGetValue(label, out var h))
            {
                EventManager.NewNotificationInvoke(Enums.Notification.ModuleNotLoaded);
                return;
            }
            _opInFlight = true;
            var token = BeginOp();
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);
            try
            {
                var unloadH = Addressables.UnloadSceneAsync(h, true);
                await unloadH.ToUniTask(cancellationToken: token);
                _loaded.Remove(label);
                EventManager.NewNotificationInvoke(Enums.Notification.ModuleUnloaded);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
            }
            catch (OperationCanceledException) { }
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
            var token = BeginOp();
            try
            {
                await UnloadOpenScenesInternalAsync(token);
            }
            finally
            {
                _opInFlight = false;
                ReportIdle();
            }
        }

        public bool ClearAllCaches()
        {
            var ok = Caching.ClearCache();
            if (!ok) EventManager.NewNotificationInvoke(Enums.Notification.CacheClearFailed);

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

        public bool IsLoaded(Enums.AddressLabel label) => _loaded.ContainsKey(label);

        public IReadOnlyList<Enums.AddressLabel> GetOpenLabels()
        {
            _tmpLabels.Clear();
            foreach (var kv in _loaded) _tmpLabels.Add(kv.Key);
            return _tmpLabels;
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
            var token = BeginOp();
            EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Starting);

            try
            {
                if (!_currentLocator.Locate(label.ToString(), typeof(SceneInstance), out var locs) ||
                    locs == null || locs.Count == 0)
                {
                    EventManager.NewNotificationInvoke(Enums.Notification.LabelNotFound);
                    throw new Exception($"Label '{label}' not found in catalog.");
                }

                var sceneLoc = ChooseSceneLocation(locs);
                var bytes    = await GetSizeAsync(sceneLoc, token);

                if (bytes > 0)
                {
                    long total = bytes;
                    long shown = 0;
                    _lastBytesShown = 0;

                    var dl = Addressables.DownloadDependenciesAsync(new List<IResourceLocation> { sceneLoc }, false);

                    await dl.ToUniTask(
                        progress: Progress.Create<float>(p =>
                        {
                            ThrottledReportDownload(p);
                            var done = (long)(total * p);
                            ThrottledBytes(done, total);
                        }),
                        cancellationToken: token
                    );

                    if (dl.Status != AsyncOperationStatus.Succeeded)
                    {
                        EventManager.NewNotificationInvoke(Enums.Notification.DependenciesDownloadFailed);
                        throw new Exception("Failed to download dependencies.");
                    }

                    Addressables.Release(dl);
                }
                else
                {
                    ReportDownloadInternal(1f, "0 B / 0 B");
                }
                var loadH = Addressables.LoadSceneAsync(sceneLoc, LoadSceneMode.Additive, activateOnLoad: false);
                await loadH.ToUniTask(progress: Progress.Create<float>(ThrottledReportLoad), cancellationToken: token);
                if (loadH.Status != AsyncOperationStatus.Succeeded) throw new Exception(loadH.OperationException != null ? loadH.OperationException.Message : "Failed to load scene");
                var inst = loadH.Result;
                await inst.ActivateAsync().ToUniTask(cancellationToken: token);
                await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate, token);
                if (!inst.Scene.isLoaded) throw new Exception("Scene activated but not reported as loaded");
                _loaded[label] = loadH;
                if (makeActive)
                {
                    if (!SceneManager.SetActiveScene(inst.Scene)) LoggerExtra.LogWarning($"[SceneService] SetActiveScene failed for {label}");
                }
                EventManager.NewNotificationInvoke(Enums.Notification.ModuleLoaded);
                EventManager.LoaderStatusChangedInvoke(Enums.LoaderStatus.Completed);
            }
            catch (OperationCanceledException) { }
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

        private async Task UnloadOpenScenesInternalAsync(CancellationToken token)
        {
            if (_loaded.Count == 0) return;

            foreach (var kv in _loaded)
            {
                var unloadH = Addressables.UnloadSceneAsync(kv.Value, true);
                await unloadH.ToUniTask(cancellationToken: token);
            }

            _loaded.Clear();
            await Resources.UnloadUnusedAssets();
            if (RunGcAfterHeavyOps) GC.Collect();
        }

        private async Task DownloadManyAndReportAsync(IResourceLocator locator, IReadOnlyList<Enums.AddressLabel> labels, CancellationToken token)
        {
            _tmpLocs.Clear();
            _tmpSizes.Clear();

            // locate + size
            long totalBytes = 0;
            var core = GetCoreLabel();

            for (int i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                if (!locator.Locate(label.ToString(), typeof(SceneInstance), out var l) || l == null || l.Count == 0)
                {
                    if (label == core) throw new Exception($"Core label '{label}' not found in catalog.");
                    LoggerExtra.LogWarning($"[SceneService] Optional label '{label}' not found. Skipping.");
                    continue;
                }

                var loc = ChooseSceneLocation(l);
                _tmpLocs.Add(loc);

                var s = await GetSizeAsync(loc, token);
                _tmpSizes.Add(s);
                totalBytes += s;
            }

            long accumulatedBytes = 0;
            float accumulated = 0f;
            _lastBytesShown = 0;

            for (int i = 0; i < _tmpLocs.Count; i++)
            {
                var loc   = _tmpLocs[i];
                var bytes = _tmpSizes[i];

                if (bytes <= 0)
                {
                    if (totalBytes > 0) accumulated += 0f;
                    ReportDownloadInternal(accumulated, totalBytes > 0 ? $"{FormatBytes(accumulatedBytes)} / {FormatBytes(totalBytes)}" : null);
                    continue;
                }
                var dl = Addressables.DownloadDependenciesAsync(new List<IResourceLocation> { loc }, false);
                await dl.ToUniTask(
                    progress: Progress.Create<float>(p =>
                    { float weighted = (totalBytes > 0)
                            ? ((float)bytes / totalBytes) * p
                            : (1f / Math.Max(1, _tmpLocs.Count)) * p;
                        float overall = accumulated + weighted;
                        ThrottledReportDownload(overall);
                        var done = accumulatedBytes + (long)(bytes * p);
                        ThrottledBytes(done, totalBytes);
                    }),
                    cancellationToken: token
                );
                if (dl.Status != AsyncOperationStatus.Succeeded) throw new Exception("Failed to download dependencies.");
                Addressables.Release(dl);
                accumulatedBytes += bytes;
                accumulated += (totalBytes > 0) ? ((float)bytes / totalBytes) : (1f / Math.Max(1, _tmpLocs.Count));
                ReportDownloadInternal(accumulated, totalBytes > 0 ? $"{FormatBytes(accumulatedBytes)} / {FormatBytes(totalBytes)}" : null);
            }
            ReportDownloadInternal(1f, totalBytes > 0 ? $"{FormatBytes(totalBytes)} / {FormatBytes(totalBytes)}" : null);
        }
        private async Task LoadManyAndReportAsync(IResourceLocator locator, IReadOnlyList<Enums.AddressLabel> labels, CancellationToken token)
        {
            _tmpLabels.Clear();
            var core = GetCoreLabel();
            var present = new List<Enums.AddressLabel>(labels.Count);
            for (int i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                if (locator.Locate(label.ToString(), typeof(SceneInstance), out var l) && l != null && l.Count > 0)
                {
                    present.Add(label);
                }
                else if (label == core)
                {
                    throw new Exception($"Core label '{label}' not found in catalog.");
                }
                else
                {
                    LoggerExtra.LogWarning($"[SceneService] Optional label '{label}' not found. Skipping.");
                }
            }

            float per = (present.Count > 0) ? (1f / present.Count) : 1f;
            float baseAccum = 0f;
            for (int i = 0; i < present.Count; i++)
            {
                var label = present[i];
                locator.Locate(label.ToString(), typeof(SceneInstance), out var l);
                var sceneLoc = ChooseSceneLocation(l);

                var loadH = Addressables.LoadSceneAsync(sceneLoc, LoadSceneMode.Additive, activateOnLoad: false);
                await loadH.ToUniTask(
                    progress: Progress.Create<float>(p => ThrottledReportLoad(baseAccum + per * p)),
                    cancellationToken: token);
                if (loadH.Status != AsyncOperationStatus.Succeeded) throw new Exception(loadH.OperationException != null ? loadH.OperationException.Message : "Failed to load scene");
                var inst = loadH.Result;
                await inst.ActivateAsync().ToUniTask(cancellationToken: token);
                await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate, token);

                if (!inst.Scene.isLoaded)
                    throw new Exception("Scene activated but not reported as loaded");
                _loaded[label] = loadH;
                if (label == core) SceneManager.SetActiveScene(inst.Scene);
                baseAccum += per;
                ThrottledReportLoad(baseAccum);
            }
            ThrottledReportLoad(1f);
        }


        private async UniTask<long> GetSizeAsync(IResourceLocation loc, CancellationToken token)
        {
            var sizeH = Addressables.GetDownloadSizeAsync(loc);
            await sizeH.ToUniTask(cancellationToken: token);
            var bytes = (sizeH.Status == AsyncOperationStatus.Succeeded) ? sizeH.Result : 0;
            if (sizeH.IsValid()) Addressables.Release(sizeH);
            return bytes;
        }

        private static IResourceLocation ChooseSceneLocation(IList<IResourceLocation> locs)
        {
            if (locs == null || locs.Count == 0) return null;
            if (locs.Count == 1) return locs[0];
            IResourceLocation best = locs[0];
            string bestKey = best.PrimaryKey ?? string.Empty;
            for (int i = 1; i < locs.Count; i++)
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

        private void ReportIdle()
        {
            EventManager.LoadProgressInvoke(Enums.LoaderPhase.Idle, 1f);
        }

        private void ThrottledReportDownload(float overall01)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastProgTs < ProgressThrottleSec) return;
            _lastProgTs = now;
            ReportDownloadInternal(overall01, null);
        }

        private void ThrottledReportLoad(float overall01)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastProgTs < ProgressThrottleSec) return;
            _lastProgTs = now;
            var p = 0.5f + Mathf.Clamp01(overall01) * 0.5f;
            EventManager.LoadProgressInvoke(Enums.LoaderPhase.SceneLoading, p);
        }

        private void ThrottledBytes(long done, long total)
        {
            float now = Time.realtimeSinceStartup;
            if ((done - _lastBytesShown) < BytesStepForText && (now - _lastProgTs) < ProgressThrottleSec) return;
            _lastBytesShown = done;
            ReportDownloadInternal(null, $"{FormatBytes(done)} / {FormatBytes(total)}");
        }
        private void ReportDownloadInternal(float? normalized01, string message)
        {
            float p = normalized01.HasValue ? (Mathf.Clamp01(normalized01.Value) * 0.5f) : -1f;
            EventManager.LoadProgressInvoke(Enums.LoaderPhase.DownloadingDependencies, p, message);
        }

        #region Events

        private void OnEnable()
        {
            EventManager.OnRequestLoadByQuality += HandleRequestByQuality;
            EventManager.OnRequestSceneLoad    += HandleRequestByScene;
        }

        private void OnDisable()
        {
            EventManager.OnRequestLoadByQuality -= HandleRequestByQuality;
            EventManager.OnRequestSceneLoad     -= HandleRequestByScene;

            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = null;
        }
        #endregion

        private async void HandleRequestByQuality(Enums.TextureQuality quality)
        {
            if (quality == CurrentTextureQuality)
            {
                EventManager.NewNotificationInvoke(Enums.Notification.NoChange);
                return;
            }
            CurrentTextureQuality = quality;
            await SwitchCatalog(GetCatalogUrl(CurrentSceneVariant, CurrentTextureQuality), preserveOpen: true);
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
            await SwitchCatalog(GetCatalogUrl(CurrentSceneVariant, CurrentTextureQuality), preserveOpen: false);
        }

        public Enums.AddressLabel GetCoreLabel()
        {
            return CurrentSceneVariant == Enums.SceneVariant.Catalog_B ? Enums.AddressLabel.B : Enums.AddressLabel.A;
        }

        public IReadOnlyList<Enums.AddressLabel> GetAvailableModuleLabels()
        {
            _tmpLabels.Clear();
            if (_currentLocator == null) return _tmpLabels;
            foreach (Enums.AddressLabel label in Enum.GetValues(typeof(Enums.AddressLabel)))
            {
                if (label == Enums.AddressLabel.A || label == Enums.AddressLabel.B) continue;
                if (_currentLocator.Locate(label.ToString(), typeof(SceneInstance), out var locs) && locs != null && locs.Count > 0)
                    _tmpLabels.Add(label);
            }
            return _tmpLabels;
        }

        private CancellationToken BeginOp()
        {
            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = new CancellationTokenSource();
            _lastProgTs = 0f;
            _lastBytesShown = 0;
            return _opCts.Token;
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
