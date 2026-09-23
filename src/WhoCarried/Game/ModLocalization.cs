using MegaCrit.Sts2.Core.Localization;
using WhoCarried.Localization;

namespace WhoCarried.Game;

/// <summary>Registers embedded resources through the game's public localization API.</summary>
internal static class ModLocalization
{
    private const string Table = "main_menu_ui";
    private static LocTable? _registered;

    public static void Install()
    {
        Loc.Lookup = Lookup;
        Loc.CurrentCulture = () => LocManager.Instance?.CultureInfo ?? System.Globalization.CultureInfo.InvariantCulture;
    }

    private static string? Lookup(string key)
    {
        LocManager? manager = LocManager.Instance;
        if (manager == null) return null;
        LocTable table = manager.GetTable(Table);
        // SetLanguage replaces tables. Register lazily, including after a language switch.
        if (!ReferenceEquals(table, _registered))
        {
            table.MergeWith(Loc.Read("eng"));
            table.MergeWith(Loc.Read(manager.Language));
            _registered = table;
        }
        return table.HasEntry(key) ? table.GetRawText(key) : null;
    }
}
