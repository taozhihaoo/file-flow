using Xunit;
using FileFlow.Core.Localization;

namespace FileFlow.Core.Tests;

/// <summary>
/// Guards the two translation dictionaries: both languages must define exactly the
/// same keys with non-empty values, otherwise the UI silently shows a key name.
/// </summary>
public class LocalizationTests
{
    [Fact]
    public void BothLanguages_DefineTheSameKeys()
    {
        var english = AppStrings.English.Keys.ToHashSet(StringComparer.Ordinal);
        var chinese = AppStrings.Chinese.Keys.ToHashSet(StringComparer.Ordinal);

        var missingInChinese = english.Except(chinese).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var missingInEnglish = chinese.Except(english).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.True(
            missingInChinese.Length == 0 && missingInEnglish.Length == 0,
            $"Missing in zh: [{string.Join(", ", missingInChinese)}]; missing in en: [{string.Join(", ", missingInEnglish)}]");
    }

    [Fact]
    public void EveryTranslation_IsNotEmpty()
    {
        foreach (var (language, strings) in new[] { ("en", AppStrings.English), ("zh", AppStrings.Chinese) })
        {
            foreach (var (key, value) in strings)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{language}: '{key}' is empty.");
            }
        }
    }

    [Fact]
    public void EveryTranslation_KeepsItsPlaceholders()
    {
        // A translation that drops {0} would throw at runtime when formatting.
        foreach (var (key, englishValue) in AppStrings.English)
        {
            var chineseValue = AppStrings.Chinese[key];
            var expected = Placeholders(englishValue);
            var actual = Placeholders(chineseValue);

            Assert.True(
                expected.SetEquals(actual),
                $"'{key}' placeholders differ: en [{string.Join(",", expected)}] vs zh [{string.Join(",", actual)}]");
        }
    }

    private static HashSet<string> Placeholders(string value)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        while (true)
        {
            var start = value.IndexOf('{', index);
            if (start < 0)
            {
                return result;
            }

            var end = value.IndexOf('}', start);
            if (end < 0)
            {
                return result;
            }

            result.Add(value[start..(end + 1)]);
            index = end + 1;
        }
    }

    [Theory]
    [InlineData(null, AppLanguages.English)]
    [InlineData("", AppLanguages.English)]
    [InlineData("en", AppLanguages.English)]
    [InlineData("zh", AppLanguages.Chinese)]
    [InlineData("zh-CN", AppLanguages.Chinese)]
    [InlineData("zh-Hans", AppLanguages.Chinese)]
    [InlineData("de-DE", AppLanguages.English)]
    public void Normalize_MapsUnknownValuesToEnglish(string? input, string expected)
    {
        Assert.Equal(expected, AppLanguages.Normalize(input));
    }
}
