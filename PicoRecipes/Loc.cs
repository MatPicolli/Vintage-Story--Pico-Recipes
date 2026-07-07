using Vintagestory.API.Config;

namespace PicoRecipes
{
    /// <summary>
    /// Localization with a built-in English fallback. If the mod's lang file did not load for some
    /// reason (wrong install layout, etc.) the UI still shows readable text instead of raw keys.
    /// </summary>
    public static class Loc
    {
        public static string T(string key, string englishFallback, params object[] args)
        {
            string full = "picorecipes:" + key;
            string translated = Lang.GetIfExists(full, args);
            if (translated != null) return translated;
            return args != null && args.Length > 0 ? string.Format(englishFallback, args) : englishFallback;
        }
    }
}
