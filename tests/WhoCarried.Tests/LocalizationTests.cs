using System.Globalization;
using System.Text.RegularExpressions;
using WhoCarried.Localization;

namespace WhoCarried.Tests;

public static class LocalizationTests
{
    [Test]
    public static void CatalogsHaveMatchingKeysAndPlaceholders()
    {
        var english = Loc.Read("eng");
        var chinese = Loc.Read("zhs");
        Check.True(english.Count > 100, "embedded English catalog");
        Check.Equal(string.Join("|", english.Keys.Order()), string.Join("|", chinese.Keys.Order()), "key parity");
        foreach (var (key, text) in english)
        {
            Check.True(key.StartsWith("WHO_CARRIED."), key);
            Check.True(!string.IsNullOrWhiteSpace(chinese[key]), key);
            string Args(string template) => string.Join(",", Regex.Matches(template, @"\{(\d+)(?:[^{}]*)\}")
                .Select(m => m.Groups[1].Value).Order());
            Check.Equal(Args(text), Args(chinese[key]), key + " placeholders");
            object[] values = Enumerable.Range(0, 12).Select(n => (object)n).ToArray();
            _ = string.Format(CultureInfo.InvariantCulture, text, values);
            _ = string.Format(CultureInfo.GetCultureInfo("zh-CN"), chinese[key], values);
        }
    }

    [Test]
    public static void DebuffCostAmountsUseCompleteTemplates()
    {
        try
        {
            foreach (string language in new[] { "eng", "zhs" })
            {
                var catalog = Loc.Read(language);
                Loc.Lookup = key => catalog.GetValueOrDefault(key);
                foreach (string suffix in new[] { "extra_taken", "less_dealt", "less_block" })
                {
                    string key = "WHO_CARRIED.debuffs." + suffix;
                    Check.True(catalog[key].Contains("{0}"), key + " owns amount placement");
                    Check.Equal(string.Format(CultureInfo.InvariantCulture, catalog[key], 42), Loc.Text(key, 42));
                }
            }
            Loc.Lookup = _ => "loss: {0}";
            Check.Equal("loss: 42", Loc.Text("WHO_CARRIED.debuffs.less_dealt", 42));
        }
        finally { Loc.Lookup = null; }
    }

    [Test]
    public static void MissingMalformedAndThrowingTranslationsFallBackSafely()
    {
        try
        {
            Loc.Lookup = _ => null;
            Check.Equal("Close", Loc.Text("WHO_CARRIED.action.close"));
            Loc.Lookup = _ => "";
            Check.Equal("Close", Loc.Text("WHO_CARRIED.action.close"));
            Loc.Lookup = _ => "broken {";
            Check.Equal("Close", Loc.Text("WHO_CARRIED.action.close"));
            Loc.Lookup = _ => throw new InvalidOperationException();
            Check.Equal("Close", Loc.Text("WHO_CARRIED.action.close"));
            Check.Equal("[WHO_CARRIED.missing]", Loc.Text("WHO_CARRIED.missing"));
            Check.Equal("[WHO_CARRIED.summary.players]", Loc.Text("WHO_CARRIED.summary.players"));
            Check.Equal(0, Loc.Read("missing_language").Count);
        }
        finally { Loc.Lookup = null; }
    }

    [Test]
    public static void ChineseTemplatesMayReorderArgumentsAndSwitchBackToEnglish()
    {
        try
        {
            var chinese = Loc.Read("zhs");
            Loc.Lookup = key => chinese.GetValueOrDefault(key);
            Check.Equal("关闭", Loc.Text("WHO_CARRIED.action.close"));
            Check.Equal("在第 43 层击败首领，15 场战斗共造成 100 点伤害。",
                Loc.Text("WHO_CARRIED.summary.won", "首领", 43, 100, "15 场战斗"));
            Loc.Lookup = null;
            Check.Equal("Close", Loc.Text("WHO_CARRIED.action.close"));
        }
        finally { Loc.Lookup = null; }
    }
}
