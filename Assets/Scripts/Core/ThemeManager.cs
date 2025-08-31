using UnityEngine;
using vCapProject.ScriptableObject;
using vCapProject.UI;

namespace vCapProject.Core 
{
    public class ThemeManager 
    {
        public ThemeManager(ThemeSwitch initial)
        {
            Current = initial;
        }
        public ThemeSwitch Current { get; private set; }

        public void ApplyTheme(ThemeSwitch theme, float tweenDuration = 0.12f) 
        {
            Current = theme;
            if (theme == null) return;

            foreach (var tag in ThemeRegistry.All) 
            {
                ApplyToTag(tag, tweenDuration);
            }
        }

        public void ApplyToTag(ThemeTag tag, float tweenDuration) 
        {
            if (tag == null || Current == null) return;
            Color target = tag.Slot switch 
            {
                Enums.ThemeSlot.Text=> Current.TextColor,
                Enums.ThemeSlot.UI=> Current.UIColor,
                _=> Current.UIColor
            };
            tag.ApplyColor(target, tweenDuration);
        }
    }
}