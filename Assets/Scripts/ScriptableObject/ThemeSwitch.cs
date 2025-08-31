using UnityEngine;

namespace vCapProject.ScriptableObject
{
    [CreateAssetMenu(menuName = "UI/Theme", fileName = "Theme")]
    public class ThemeSwitch : UnityEngine.ScriptableObject 
    {
        [Header("Colors")]
        public Color UIColor= new(0.13f, 0.13f, 0.16f, 0.95f);
        public Color TextColor = Color.white;
    }
}