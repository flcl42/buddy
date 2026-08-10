using System.Collections.Specialized;
using Buddy.App.ViewModels;

namespace Buddy.App.Tests;

public sealed class DialogScrollFollowStateTests
{
    [Fact]
    public void AppendedMessageFollowsWhenAlreadyAtBottom()
    {
        DialogScrollFollowState state = new();
        NotifyCollectionChangedEventArgs addition = CreateAddition(index: 4);

        bool shouldFollow = state.ShouldFollowAppend(addition, totalCount: 5);

        Assert.True(shouldFollow);
    }

    [Fact]
    public void AppendedMessagePreservesPositionAfterUserScrollsUp()
    {
        DialogScrollFollowState state = new();
        state.UpdatePosition(isAtBottom: false);

        bool shouldFollow = state.ShouldFollowAppend(
            CreateAddition(index: 4),
            totalCount: 5);

        Assert.False(shouldFollow);
    }

    [Fact]
    public void ReturningToBottomRestoresFollowing()
    {
        DialogScrollFollowState state = new();
        state.UpdatePosition(isAtBottom: false);
        state.UpdatePosition(isAtBottom: true);

        bool shouldFollow = state.ShouldFollowAppend(
            CreateAddition(index: 4),
            totalCount: 5);

        Assert.True(shouldFollow);
    }

    [Fact]
    public void NonTailInsertionNeverForcesScroll()
    {
        DialogScrollFollowState state = new();

        bool shouldFollow = state.ShouldFollowAppend(
            CreateAddition(index: 1),
            totalCount: 5);

        Assert.False(shouldFollow);
    }

    [Fact]
    public void EmptyConversationResetsFollowingForTheNextSession()
    {
        DialogScrollFollowState state = new();
        state.UpdatePosition(isAtBottom: false);

        state.Reset();

        Assert.True(state.IsFollowing);
        Assert.False(state.HasUnseenTail);
    }

    [Fact]
    public void AppendedMessageMarksUnseenTailWhileReadingEarlierMessages()
    {
        DialogScrollFollowState state = new();
        state.UpdatePosition(isAtBottom: false);

        bool shouldFollow = state.ShouldFollowAppend(
            CreateAddition(index: 4),
            totalCount: 5);

        Assert.False(shouldFollow);
        Assert.True(state.HasUnseenTail);
    }

    [Fact]
    public void TailContentGrowthFollowsOnlyWhenAlreadyFollowing()
    {
        DialogScrollFollowState state = new();

        Assert.True(state.ShouldFollowTailContentChange(
            changedIndex: 4,
            totalCount: 5));

        state.UpdatePosition(isAtBottom: false);

        Assert.False(state.ShouldFollowTailContentChange(
            changedIndex: 4,
            totalCount: 5));
        Assert.True(state.HasUnseenTail);
    }

    [Fact]
    public void NonTailContentGrowthDoesNotCreateAnUnseenTail()
    {
        DialogScrollFollowState state = new();
        state.UpdatePosition(isAtBottom: false);

        Assert.False(state.ShouldFollowTailContentChange(
            changedIndex: 2,
            totalCount: 5));
        Assert.False(state.HasUnseenTail);
    }

    [Fact]
    public void FollowLatestClearsTheUnseenTail()
    {
        DialogScrollFollowState state = new();
        state.UpdatePosition(isAtBottom: false);
        state.ShouldFollowAppend(CreateAddition(index: 4), totalCount: 5);

        state.FollowLatest();

        Assert.True(state.IsFollowing);
        Assert.False(state.HasUnseenTail);
    }

    private static NotifyCollectionChangedEventArgs CreateAddition(int index)
    {
        return new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            changedItem: new object(),
            index);
    }
}
