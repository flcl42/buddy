namespace Buddy.App.ViewModels;

public enum SpeakMode
{
    Dialog = 0,
    Monologue = 1,
}

public static class SpeakNavigationState
{
    public const int SpeakTabIndex = 0;

    public const int RecordingsTabIndex = 1;

    public static bool IsRecordings(int selectedTabIndex) =>
        selectedTabIndex == RecordingsTabIndex;

    public static bool IsSpeak(int selectedTabIndex) =>
        selectedTabIndex == SpeakTabIndex;

    public static bool IsChooser(
        int selectedTabIndex,
        SpeakMode? selectedMode) =>
        IsSpeak(selectedTabIndex) && selectedMode is null;

    public static bool IsMonologue(
        int selectedTabIndex,
        SpeakMode? selectedMode) =>
        IsSpeak(selectedTabIndex) && selectedMode == SpeakMode.Monologue;

    public static bool IsDialog(
        int selectedTabIndex,
        SpeakMode? selectedMode) =>
        IsSpeak(selectedTabIndex) && selectedMode == SpeakMode.Dialog;
}
