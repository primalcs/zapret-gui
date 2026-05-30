using System.Windows.Media;

namespace zapret_gui;

public enum AppLanguage
{
    En,
    Ru,
}

public sealed record LanguageOption(AppLanguage Language, ImageSource FlagIcon, string Code)
{
    public static LanguageOption English { get; } = new(AppLanguage.En, FlagIcons.Us, "EN");

    public static LanguageOption Russian { get; } = new(AppLanguage.Ru, FlagIcons.Ru, "RU");

    public static IReadOnlyList<LanguageOption> All { get; } = [English, Russian];
}
