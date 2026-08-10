using Buddy.Core.Domain;

namespace Buddy.Persistence.Tests;

public sealed class SqliteAppSettingsStoreTests
{
    [Fact]
    public async Task StringValuesRoundTripUpdateAndRemove()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteAppSettingsStore settings = new(store.Connections);

        Assert.Null(await settings.GetAsync(BuddySettings.InputDeviceId));

        await settings.SetAsync(BuddySettings.InputDeviceId, "headset-\"primary\"");
        Assert.Equal(
            "headset-\"primary\"",
            await settings.GetAsync(BuddySettings.InputDeviceId));

        await settings.SetAsync(BuddySettings.InputDeviceId, "conference-mic");
        Assert.Equal(
            "conference-mic",
            await settings.GetAsync(BuddySettings.InputDeviceId));

        await settings.SetAsync(BuddySettings.OutputDeviceId, "desk-speakers");
        await settings.SetAsync(
            BuddySettings.DialogAllowedPauseMilliseconds,
            "3000");
        Assert.Equal(
            "desk-speakers",
            await settings.GetAsync(BuddySettings.OutputDeviceId));
        Assert.Equal(
            "conference-mic",
            await settings.GetAsync(BuddySettings.InputDeviceId));
        Assert.Equal(
            "3000",
            await settings.GetAsync(
                BuddySettings.DialogAllowedPauseMilliseconds));

        await settings.RemoveAsync(BuddySettings.InputDeviceId);
        Assert.Null(await settings.GetAsync(BuddySettings.InputDeviceId));
        Assert.Equal(
            "desk-speakers",
            await settings.GetAsync(BuddySettings.OutputDeviceId));
    }
}
