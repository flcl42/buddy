using System.Collections.Specialized;

namespace Buddy.App.ViewModels;

internal sealed class DialogScrollFollowState
{
    public bool IsFollowing { get; private set; } = true;

    public bool HasUnseenTail { get; private set; }

    public void UpdatePosition(bool isAtBottom)
    {
        IsFollowing = isAtBottom;
        if (isAtBottom)
        {
            HasUnseenTail = false;
        }
    }

    public void FollowLatest()
    {
        IsFollowing = true;
        HasUnseenTail = false;
    }

    public void Reset()
    {
        FollowLatest();
    }

    public bool ShouldFollowAppend(
        NotifyCollectionChangedEventArgs eventArgs,
        int totalCount)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);

        bool isTailAppend = eventArgs.Action == NotifyCollectionChangedAction.Add
            && eventArgs.NewItems is { Count: > 0 }
            && eventArgs.NewStartingIndex >= 0
            && eventArgs.NewStartingIndex + eventArgs.NewItems.Count == totalCount;

        if (!isTailAppend)
        {
            return false;
        }

        if (!IsFollowing)
        {
            HasUnseenTail = true;
        }

        return IsFollowing;
    }

    public bool ShouldFollowTailContentChange(
        int changedIndex,
        int totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        if (changedIndex < 0 || changedIndex >= totalCount)
        {
            return false;
        }

        bool isTail = changedIndex == totalCount - 1;
        if (isTail && !IsFollowing)
        {
            HasUnseenTail = true;
        }

        return isTail && IsFollowing;
    }
}
