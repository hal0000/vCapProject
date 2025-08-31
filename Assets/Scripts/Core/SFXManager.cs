using System.Collections.Generic;
using UnityEngine;

namespace vCapProject.Core
{
    [DefaultExecutionOrder(-1000)]
    public class SFXManager : MonoBehaviour
    {
        public static SFXManager I { get; private set; }

        [Header("Clips")]
        public AudioClip ClickClip;
        public AudioClip DialogOpenClip;
        public AudioClip DialogCloseClip;
        public AudioClip NotificationPop;

        [Header("Settings")]
        [Range(0f, 1f)] public float Volume = 1f;// TEK AYAR
        [SerializeField, Min(1)] int Voices = 4;// eşzamanlı tıklar için hafif havuz

        readonly List<AudioSource> Pool = new List<AudioSource>(8);

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject($"SFX_{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;
                src.volume = Volume;
                Pool.Add(src);
            }
        }
        /// <summary>
        /// todo implement?
        /// </summary>
        /// <param name="v"></param>
        public void SetVolume(float v)
        {
            Volume = Mathf.Clamp01(v);
            for (int i = 0; i < Pool.Count; i++) Pool[i].volume = Volume;
        }

        public void PlayClick(float scale = 1f)=> PlayOneShot(ClickClip, scale);
        public void PlayDialogOpen(float scale = 1f)=> PlayOneShot(DialogOpenClip, scale);
        public void PlayDialogClose(float scale = 1f)=> PlayOneShot(DialogCloseClip, scale);
        public void PlayPopUp(float scale = 1f)=> PlayOneShot(NotificationPop, scale);

        void PlayOneShot(AudioClip clip, float scale)
        {
            if (!clip) return;
            var src = GetFree();
            src.PlayOneShot(clip, Mathf.Clamp01(scale) * Volume);
        }

        AudioSource GetFree()
        {
            for (int i = 0; i < Pool.Count; i++) if (!Pool[i].isPlaying) return Pool[i];
            return Pool[0];
        }
    }
}
