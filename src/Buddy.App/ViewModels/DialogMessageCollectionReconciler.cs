using System.Collections.ObjectModel;
using Buddy.Core.Domain;

namespace Buddy.App.ViewModels;

internal static class DialogMessageCollectionReconciler
{
    public static void Reconcile(
        ObservableCollection<DialogMessageViewModel> target,
        IReadOnlyList<DialogMessage> source,
        IReadOnlyDictionary<Guid, DialogPronunciationAssessment>?
            pronunciations = null,
        Guid? recordingId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        int matching = 0;
        while (matching < target.Count
            && matching < source.Count
            && target[matching].Id == source[matching].Id)
        {
            DialogMessage message = source[matching];
            target[matching].UpdateFrom(
                message,
                GetPronunciation(message.Id, pronunciations),
                recordingId);
            matching++;
        }

        while (target.Count > matching)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (int index = matching; index < source.Count; index++)
        {
            DialogMessage message = source[index];
            target.Add(
                new DialogMessageViewModel(
                    message,
                    false,
                    GetPronunciation(message.Id, pronunciations),
                    recordingId));
        }
    }

    private static DialogPronunciationAssessment? GetPronunciation(
        Guid messageId,
        IReadOnlyDictionary<Guid, DialogPronunciationAssessment>?
            pronunciations)
    {
        return pronunciations is not null
            && pronunciations.TryGetValue(messageId, out var assessment)
                ? assessment
                : null;
    }
}
