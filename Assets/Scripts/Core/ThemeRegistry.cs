using System.Collections.Generic;
using vCapProject.UI;

namespace vCapProject.Core 
{
    public static class ThemeRegistry 
    {
        static readonly HashSet<ThemeTag> _tags = new HashSet<ThemeTag>();

        public static void Register(ThemeTag tag) 
        {
            if (tag == null) return;
            _tags.Add(tag);
        }

        public static void Unregister(ThemeTag tag) 
        {
            if (tag == null) return;
            _tags.Remove(tag);
        }

        public static IEnumerable<ThemeTag> All => _tags;
        public static int Count => _tags.Count;
    }
}