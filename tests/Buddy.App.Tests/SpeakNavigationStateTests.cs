using Buddy.App.ViewModels;

namespace Buddy.App.Tests;

public sealed class SpeakNavigationStateTests
{
    [Fact]
    public void SpeakChooserIsTheDefaultNavigationState()
    {
        Assert.Equal(0, SpeakNavigationState.SpeakTabIndex);
        Assert.Equal(1, SpeakNavigationState.RecordingsTabIndex);
        Assert.Equal(0, (int)SpeakMode.Dialog);
        Assert.True(SpeakNavigationState.IsChooser(
            selectedTabIndex: 0,
            selectedMode: null));
        Assert.False(SpeakNavigationState.IsDialog(
            selectedTabIndex: 0,
            selectedMode: null));
        Assert.False(SpeakNavigationState.IsMonologue(
            selectedTabIndex: 0,
            selectedMode: null));
    }

    [Theory]
    [InlineData(SpeakMode.Monologue)]
    [InlineData(SpeakMode.Dialog)]
    public void RecordingsTabHidesBothSpeakModes(SpeakMode mode)
    {
        Assert.True(SpeakNavigationState.IsRecordings(
            SpeakNavigationState.RecordingsTabIndex));
        Assert.False(SpeakNavigationState.IsSpeak(
            SpeakNavigationState.RecordingsTabIndex));
        Assert.False(SpeakNavigationState.IsChooser(
            SpeakNavigationState.RecordingsTabIndex,
            mode));
        Assert.False(SpeakNavigationState.IsMonologue(
            SpeakNavigationState.RecordingsTabIndex,
            mode));
        Assert.False(SpeakNavigationState.IsDialog(
            SpeakNavigationState.RecordingsTabIndex,
            mode));
    }

    [Fact]
    public void RecordingsTabAlsoHidesTheChooser()
    {
        Assert.False(SpeakNavigationState.IsChooser(
            SpeakNavigationState.RecordingsTabIndex,
            selectedMode: null));
    }

    [Fact]
    public void SpeakTabShowsOnlyMonologueForMonologueMode()
    {
        Assert.True(SpeakNavigationState.IsSpeak(
            SpeakNavigationState.SpeakTabIndex));
        Assert.True(SpeakNavigationState.IsMonologue(
            SpeakNavigationState.SpeakTabIndex,
            SpeakMode.Monologue));
        Assert.False(SpeakNavigationState.IsDialog(
            SpeakNavigationState.SpeakTabIndex,
            SpeakMode.Monologue));
    }

    [Fact]
    public void SpeakTabShowsOnlyDialogForDialogMode()
    {
        Assert.True(SpeakNavigationState.IsSpeak(
            SpeakNavigationState.SpeakTabIndex));
        Assert.False(SpeakNavigationState.IsMonologue(
            SpeakNavigationState.SpeakTabIndex,
            SpeakMode.Dialog));
        Assert.True(SpeakNavigationState.IsDialog(
            SpeakNavigationState.SpeakTabIndex,
            SpeakMode.Dialog));
    }
}
