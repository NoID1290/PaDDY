using System;

namespace PaDDY
{
    /// <summary>
    /// A user-defined pad page (tab) in the soundboard. Recordings are assigned to a
    /// page via <c>RecordingStore</c> (the <c>pad_page</c> column). The first page in
    /// <see cref="AppSettings.PadPages"/> carries the legacy "Favorites" semantics.
    /// </summary>
    public class PadPage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Page";
        // Sort/display order among pages.
        public int Order { get; set; } = 0;
        // True for the built-in Favorites page (cannot be deleted/renamed).
        public bool IsFavorites { get; set; } = false;
    }
}
