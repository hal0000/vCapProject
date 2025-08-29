using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TestLoader : MonoBehaviour
{
    [Header("Catalog URLs (.bin)")] public string url512;
    public string url1024;
    public string url2048;

    [Header("Addressables scene label (unique)")]
    public string sceneLabel = "A";

    [Header("Boot quality (1=512, 2=1024, 3=2048)")] [Range(1, 3)]
    public int bootQuality = 2;

    // state
    private string _currentUrl = null;
    private bool _isSwitching = false;

    // For proper cleanup:
    private IResourceLocator _currentLocator = null;
    private AsyncOperationHandle<IResourceLocator>? _currentCatalogHandle = null;

    // Scene lifecycle
    private SceneInstance _currentScene;
    private bool _hasScene = false;

    private async void Start()
    {
        var bootUrl = bootQuality == 1 ? url512 : bootQuality == 3 ? url2048 : url1024;
        await SwitchQualitySafe(bootUrl);
    }

#if UNITY_EDITOR
    private async void Update()
    {
        if (_isSwitching) return;

        var key = CheckQualityHotkey();
        if (key == 1) await SwitchQualitySafe(url512);
        else if (key == 2) await SwitchQualitySafe(url1024);
        else if (key == 3) await SwitchQualitySafe(url2048);
    }

    private int CheckQualityHotkey()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return 0;
        if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) return 1;
        if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) return 2;
        if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) return 3;
#endif
        return 0;
    }
#endif

    public async Task SwitchQualitySafe(string catalogUrl)
    {
        if (string.IsNullOrEmpty(catalogUrl))
        {
            LoggerExtra.LogWarning("[Loader] Empty URL. Abort.");
            return;
        }

        if (_isSwitching)
        {
            LoggerExtra.Log("[Loader] Switch already in progress. Ignored.");
            return;
        }

        if (_currentUrl == catalogUrl)
        {
            LoggerExtra.Log("[Loader] Same catalog already active. No-op.");
            return;
        }

        _isSwitching = true;
        LoggerExtra.Log($"[Loader] Switch START → {catalogUrl}");

        // 1) Load new catalog (keep handle; do NOT auto release)
        var catH =
            Addressables.LoadContentCatalogAsync(catalogUrl, false);
        await catH.Task;

        if (catH.Status != AsyncOperationStatus.Succeeded || catH.Result == null)
        {
            LoggerExtra.LogError($"[Loader] Catalog failed: {catalogUrl}");
            SafeRelease(catH);
            _isSwitching = false;
            return;
        }

        var newLocator = catH.Result;
        LoggerExtra.Log($"[Loader] Catalog loaded: {catalogUrl}");

        // 2) Locate scenes from the *new* locator only
        if (!newLocator.Locate(sceneLabel, typeof(SceneInstance), out var newSceneLocs) ||
            newSceneLocs == null || newSceneLocs.Count == 0)
        {
            LoggerExtra.LogError($"[Loader] No scenes with label '{sceneLabel}' in NEW catalog. Rollback.");
            Addressables.RemoveResourceLocator(newLocator);
            SafeRelease(catH);
            _isSwitching = false;
            return;
        }

        // Choose deterministically (by primary key string)
        var sceneLoc = ChooseSceneLocation(newSceneLocs);

        // 3) Load scene additively from the chosen location
        var loadH = Addressables.LoadSceneAsync(sceneLoc, LoadSceneMode.Additive, true);
        await loadH.Task;

        if (loadH.Status != AsyncOperationStatus.Succeeded || !loadH.Result.Scene.isLoaded)
        {
            LoggerExtra.LogError("[Loader] New scene failed to load. Rollback.");
            Addressables.RemoveResourceLocator(newLocator);
            SafeRelease(catH);
            SafeRelease(loadH);
            _isSwitching = false;
            return;
        }

        var newScene = loadH.Result;
        LoggerExtra.Log($"[Loader] New scene loaded: {newScene.Scene.name}");

        // 3.5) Make it active (optional but recommended)
        SceneManager.SetActiveScene(newScene.Scene);

        // 4) Now it’s safe to unload the previous scene + remove previous catalog
        if (_hasScene && _currentScene.Scene.isLoaded)
        {
            var unH = Addressables.UnloadSceneAsync(_currentScene, true);
            await unH.Task;
            SafeRelease(unH);
            LoggerExtra.Log("[Loader] Previous scene unloaded.");
        }

        if (_currentLocator != null)
        {
            Addressables.RemoveResourceLocator(_currentLocator);
            _currentLocator = null;
            LoggerExtra.Log("[Loader] Previous catalog locator removed.");
        }

        if (_currentCatalogHandle.HasValue)
        {
            SafeRelease(_currentCatalogHandle.Value);
            _currentCatalogHandle = null;
        }

        // 5) Commit new state
        _currentScene = newScene;
        _hasScene = true;
        _currentLocator = newLocator;
        _currentCatalogHandle = catH;
        _currentUrl = catalogUrl;

        // 6) Housekeeping
        await Resources.UnloadUnusedAssets();
        GC.Collect();

        LoggerExtra.Log($"[Loader] Switch DONE → {_currentUrl}");
        _isSwitching = false;
    }

    private static IResourceLocation ChooseSceneLocation(IList<IResourceLocation> locs)
    {
        if (locs.Count == 1) return locs[0];
        // Stable choice by primary key
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

    private void OnDestroy()
    {
        // Don’t await; playmode stop can kill tasks mid-flight.
        _ = TeardownAsync();
    }

    private async Task TeardownAsync()
    {
        try
        {
            if (_hasScene && _currentScene.Scene.isLoaded)
            {
                var unH = Addressables.UnloadSceneAsync(_currentScene, true);
                await unH.Task;
                SafeRelease(unH);
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

            await Resources.UnloadUnusedAssets();
        }
        catch (Exception e)
        {
            LoggerExtra.LogError(e.ToString());
        }
    }

    private static void SafeRelease<T>(AsyncOperationHandle<T> h)
    {
        if (h.IsValid()) Addressables.Release(h);
    }

    private static void SafeRelease(AsyncOperationHandle h)
    {
        if (h.IsValid()) Addressables.Release(h);
    }
}