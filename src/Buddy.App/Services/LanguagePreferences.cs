using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.App.Services;

public sealed record InterfaceLanguageOption(
    string Id,
    string NativeName);

public sealed record DialogLanguageOption(
    string Id,
    string EnglishName,
    string Locale,
    string WhisperLanguage,
    string InitialPrompt,
    string DisplayNameResourceKey);

public sealed class LanguagePreferences
{
    public const string DefaultInterfaceLanguageId = "en";
    public const string DefaultDialogLanguageId = "en";

    private static readonly IReadOnlyList<InterfaceLanguageOption> InterfaceOptions =
    [
        new("en", "English"),
        new("be", "Беларуская"),
        new("ru", "Русский"),
    ];

    private static readonly IReadOnlyList<DialogLanguageOption> DialogOptions =
    [
        new(
            "en",
            "English",
            "en-US",
            "en",
            "Natural spoken English with accurate punctuation.",
            "LanguageEnglish"),
        new(
            "de",
            "German",
            "de-DE",
            "de",
            "Natürlich gesprochenes Deutsch mit genauer Zeichensetzung.",
            "LanguageGerman"),
        new(
            "es",
            "Spanish",
            "es-ES",
            "es",
            "Español hablado natural con puntuación precisa.",
            "LanguageSpanish"),
        new(
            "fr",
            "French",
            "fr-FR",
            "fr",
            "Français parlé naturel avec une ponctuation précise.",
            "LanguageFrench"),
        new(
            "be",
            "Belarusian",
            "be-BY",
            "be",
            "Натуральная беларуская гаворка з дакладнай пунктуацыяй.",
            "LanguageBelarusian"),
    ];

    private readonly IAppSettingsStore _settings;
    private readonly UiLocalizationService _localization;
    private readonly IReadOnlyList<InterfaceLanguageOption> _interfaceOptions =
        InterfaceOptions;
    private readonly IReadOnlyList<DialogLanguageOption> _dialogOptions =
        DialogOptions;

    public LanguagePreferences(
        IAppSettingsStore settings,
        UiLocalizationService localization)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
    }

    public IReadOnlyList<InterfaceLanguageOption> AvailableInterfaceLanguages =>
        _interfaceOptions;

    public IReadOnlyList<DialogLanguageOption> AvailableDialogLanguages =>
        _dialogOptions;

    public string InterfaceLanguageId { get; private set; } =
        DefaultInterfaceLanguageId;

    public DialogLanguageOption DialogLanguage { get; private set; } =
        DialogOptions[0];

    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        string? interfaceLanguageId = await _settings
            .GetAsync(BuddySettings.InterfaceLanguageId, cancellationToken)
            .ConfigureAwait(false);
        InterfaceLanguageOption selectedInterface = InterfaceOptions.FirstOrDefault(
                option => string.Equals(
                    option.Id,
                    interfaceLanguageId,
                    StringComparison.Ordinal))
            ?? InterfaceOptions[0];

        string? dialogLanguageId = await _settings
            .GetAsync(BuddySettings.DialogLanguageId, cancellationToken)
            .ConfigureAwait(false);
        DialogLanguageOption selectedDialog = DialogOptions.FirstOrDefault(
                option => string.Equals(
                    option.Id,
                    dialogLanguageId,
                    StringComparison.Ordinal))
            ?? DialogOptions[0];

        InterfaceLanguageId = selectedInterface.Id;
        DialogLanguage = selectedDialog;
        _localization.Apply(InterfaceLanguageId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetInterfaceLanguageAsync(
        string languageId,
        CancellationToken cancellationToken = default)
    {
        InterfaceLanguageOption selected = InterfaceOptions.FirstOrDefault(
                option => string.Equals(option.Id, languageId, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(
                nameof(languageId),
                languageId,
                "Unsupported interface language.");
        InterfaceLanguageId = selected.Id;
        _localization.Apply(selected.Id);
        await _settings
            .SetAsync(BuddySettings.InterfaceLanguageId, selected.Id, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetDialogLanguageAsync(
        string languageId,
        CancellationToken cancellationToken = default)
    {
        DialogLanguageOption selected = DialogOptions.FirstOrDefault(
                option => string.Equals(option.Id, languageId, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(
                nameof(languageId),
                languageId,
                "Unsupported dialog language.");
        DialogLanguage = selected;
        await _settings
            .SetAsync(BuddySettings.DialogLanguageId, selected.Id, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
