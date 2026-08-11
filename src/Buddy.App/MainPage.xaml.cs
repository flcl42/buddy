#if WINDOWS
using System.Collections.Specialized;
using System.ComponentModel;
using Buddy.App.State;
using Buddy.App.ViewModels;
using Buddy.App.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Buddy.App;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly DialogScrollFollowState _dialogScrollFollow = new();
    private readonly HashSet<DialogMessageViewModel> _observedDialogMessages = [];
    private System.Drawing.Icon? _renderedTrayIcon;
    private DependencyObject? _dialogNativeRoot;
    private ScrollViewer? _dialogNativeScrollViewer;
    private bool _loaded;
    private bool _dialogScrollScheduled;
    private bool _dialogAutoScrollActive;
    private int _dialogScrollGeneration;
    private double _dialogObservedVerticalOffset = -1;
    private double _dialogObservedScrollableHeight = -1;

    public MainPage(MainViewModel viewModel)
    {
        StartupDiagnostics.Write("MainPage constructor before InitializeComponent");
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        BindingContext = _viewModel;
        _viewModel.Dialog.Messages.CollectionChanged += OnDialogMessagesChanged;
        SynchronizeDialogMessageSubscriptions();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Runtime.PropertyChanged += OnRuntimePropertyChanged;
        UpdateTrayIcon();
        StartupDiagnostics.Write("MainPage constructor complete");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        StartupDiagnostics.Write("MainPage InitializeAsync starting");
        await _viewModel.InitializeAsync();
        if (_viewModel.IsDialogMode
            && _viewModel.Dialog.Messages.Count > 0
            && _dialogScrollFollow.IsFollowing)
        {
            // Loading an existing session can append many messages before the
            // CollectionView has measured its final extent. Run one definitive
            // follow pass after initialization so the default view opens at
            // the actual latest turn.
            QueueDialogScrollToBottom();
        }

        StartupDiagnostics.Write("MainPage InitializeAsync complete");
    }

    private void OnDialogMessagesScrolled(
        object? sender,
        ItemsViewScrolledEventArgs eventArgs)
    {
        ScrollViewer? nativeScroller = ResolveDialogScrollViewer();
        bool isAtBottom = IsDialogAtBottom(eventArgs, nativeScroller);
        if (_dialogAutoScrollActive || _dialogScrollScheduled)
        {
            ObserveDialogScrollMetrics(nativeScroller);
            if (isAtBottom)
            {
                _dialogScrollFollow.UpdatePosition(isAtBottom: true);
            }

            UpdateDialogJumpToLatestButton();
            return;
        }

        if (ContinueFollowingAfterExtentGrowth(nativeScroller, isAtBottom))
        {
            return;
        }

        _dialogScrollFollow.UpdatePosition(isAtBottom);
        UpdateDialogJumpToLatestButton();
    }

    private void OnDialogMessagesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs)
    {
        SynchronizeDialogMessageSubscriptions();
        int count = _viewModel.Dialog.Messages.Count;
        if (count == 0)
        {
            CancelDialogScroll();
            _dialogScrollFollow.Reset();
            UpdateDialogJumpToLatestButton();
            return;
        }

        if (_dialogScrollFollow.ShouldFollowAppend(eventArgs, count))
        {
            QueueDialogScrollToBottom();
        }

        UpdateDialogJumpToLatestButton();
    }

    private void OnDialogMessagePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (sender is not DialogMessageViewModel message
            || !AffectsDialogMessageHeight(eventArgs.PropertyName))
        {
            return;
        }

        int count = _viewModel.Dialog.Messages.Count;
        int index = _viewModel.Dialog.Messages.IndexOf(message);
        if (_dialogScrollFollow.ShouldFollowTailContentChange(index, count))
        {
            QueueDialogScrollToBottom();
        }

        UpdateDialogJumpToLatestButton();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if ((eventArgs.PropertyName == nameof(MainViewModel.SelectedTabIndex)
                || eventArgs.PropertyName == nameof(MainViewModel.SelectedSpeakMode))
            && _viewModel.IsDialogMode)
        {
            if (_dialogScrollFollow.IsFollowing)
            {
                QueueDialogScrollToBottom();
            }
        }
        else if (eventArgs.PropertyName == nameof(MainViewModel.SelectedTabIndex)
            || eventArgs.PropertyName == nameof(MainViewModel.SelectedSpeakMode))
        {
            CancelDialogScroll();
        }

        if (eventArgs.PropertyName == nameof(MainViewModel.SelectedTabIndex)
            || eventArgs.PropertyName == nameof(MainViewModel.SelectedSpeakMode))
        {
            UpdateDialogJumpToLatestButton();
        }
    }

    private void OnDialogMessagesHandlerChanged(object? sender, EventArgs eventArgs)
    {
        ResetDialogNativeScrollViewer();
        ResolveDialogScrollViewer();
        if (_viewModel.IsDialogMode && _dialogScrollFollow.IsFollowing)
        {
            QueueDialogScrollToBottom();
        }
    }

    private void OnDialogJumpToLatestClicked(object? sender, EventArgs eventArgs)
    {
        _dialogScrollFollow.FollowLatest();
        UpdateDialogJumpToLatestButton();
        QueueDialogScrollToBottom();
    }

    private void OnRuntimePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(BuddyRuntimeState.Mode))
        {
            Dispatcher.Dispatch(UpdateTrayIcon);
        }
    }

    private void UpdateTrayIcon()
    {
        System.Drawing.Icon nextIcon =
            TrayIconRenderer.Create(_viewModel.Runtime.Mode);
        System.Drawing.Icon? previousIcon = _renderedTrayIcon;
        _renderedTrayIcon = nextIcon;
        TrayIcon.Icon = nextIcon;
        previousIcon?.Dispose();
    }

    private void QueueDialogScrollToBottom()
    {
        if (!_dialogScrollFollow.IsFollowing)
        {
            UpdateDialogJumpToLatestButton();
            return;
        }

        int generation = ++_dialogScrollGeneration;
        _dialogScrollScheduled = true;
        _dialogAutoScrollActive = true;
        DialogMessages.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(45),
            () => StartDialogScroll(generation));
    }

    private void StartDialogScroll(int generation)
    {
        if (!CanRunDialogScroll(generation))
        {
            CompleteDialogScroll(generation);
            return;
        }

        int count = _viewModel.Dialog.Messages.Count;
        DialogMessages.ScrollTo(
            count - 1,
            groupIndex: -1,
            ScrollToPosition.End,
            animate: _loaded);

        DialogMessages.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(240),
            () => SettleDialogScroll(
                generation,
                attemptsRemaining: 24,
                previousExtent: -1,
                stableBottomPasses: 0));
    }

    private void SettleDialogScroll(
        int generation,
        int attemptsRemaining,
        double previousExtent,
        int stableBottomPasses)
    {
        if (!CanRunDialogScroll(generation))
        {
            CompleteDialogScroll(generation);
            return;
        }

        // A CollectionView can create more than one native ScrollViewer. At
        // handler creation they all report a zero extent, so the first choice
        // is provisional. Re-evaluate while content is being realized so the
        // convergence loop targets the actual vertical message list.
        ScrollViewer? nativeScroller = ResolveDialogScrollViewer(
            refreshSelection: true);
        double currentExtent = nativeScroller?.ScrollableHeight ?? -1;
        bool extentIsStable = previousExtent >= 0
            && currentExtent >= 0
            && Math.Abs(currentExtent - previousExtent) <= 0.5;
        bool isAtBottom = nativeScroller is not null
            && IsNativeDialogAtBottom(nativeScroller, tolerance: 1);
        int nextStableBottomPasses = extentIsStable && isAtBottom
            ? stableBottomPasses + 1
            : 0;
        if (nextStableBottomPasses >= 6)
        {
            CompleteDialogScroll(generation);
            return;
        }

        // Once both the extent and offset look settled, observe without
        // issuing another ScrollTo. WinUI can apply a delayed realization
        // correction after each request; repeatedly nudging here would keep
        // moving that correction beyond the convergence window.
        if (!extentIsStable || !isAtBottom)
        {
            DialogMessages.ScrollTo(
                _viewModel.Dialog.Messages.Count - 1,
                groupIndex: -1,
                ScrollToPosition.End,
                animate: false);
            if (nativeScroller is not null)
            {
                nativeScroller.ChangeView(
                    horizontalOffset: null,
                    nativeScroller.ScrollableHeight,
                    zoomFactor: null,
                    disableAnimation: true);
            }
        }

        if (attemptsRemaining > 1)
        {
            DialogMessages.Dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(90),
                () => SettleDialogScroll(
                    generation,
                    attemptsRemaining - 1,
                    currentExtent,
                    nextStableBottomPasses));
            return;
        }

        DialogMessages.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(80),
            () => CompleteDialogScroll(generation));
    }

    private bool CanRunDialogScroll(int generation)
    {
        return generation == _dialogScrollGeneration
            && _dialogScrollFollow.IsFollowing
            && _viewModel.IsDialogMode
            && DialogMessages.IsVisible
            && DialogMessages.Handler is not null
            && _viewModel.Dialog.Messages.Count > 0;
    }

    private void CompleteDialogScroll(int generation)
    {
        if (generation != _dialogScrollGeneration)
        {
            return;
        }

        ScrollViewer? nativeScroller = ResolveDialogScrollViewer(
            refreshSelection: true);
        bool isAtBottom = IsDialogCurrentlyAtBottom(nativeScroller);
        ObserveDialogScrollMetrics(nativeScroller);
        _dialogScrollScheduled = false;
        _dialogAutoScrollActive = false;
        _dialogScrollFollow.UpdatePosition(isAtBottom);
        UpdateDialogJumpToLatestButton();
    }

    private void CancelDialogScroll()
    {
        _dialogScrollGeneration++;
        _dialogScrollScheduled = false;
        _dialogAutoScrollActive = false;
    }

    private bool IsDialogAtBottom(
        ItemsViewScrolledEventArgs eventArgs,
        ScrollViewer? nativeScroller)
    {
        if (nativeScroller is not null
            && nativeScroller.ScrollableHeight > 0)
        {
            return IsNativeDialogAtBottom(nativeScroller);
        }

        int count = _viewModel.Dialog.Messages.Count;
        return count == 0 || eventArgs.LastVisibleItemIndex >= count - 1;
    }

    private bool IsDialogCurrentlyAtBottom(ScrollViewer? nativeScroller = null)
    {
        nativeScroller ??= ResolveDialogScrollViewer();
        if (nativeScroller is null || nativeScroller.ScrollableHeight <= 0)
        {
            return true;
        }

        return IsNativeDialogAtBottom(nativeScroller);
    }

    private ScrollViewer? ResolveDialogScrollViewer(
        bool refreshSelection = false)
    {
        DependencyObject? platformRoot =
            DialogMessages.Handler?.PlatformView as DependencyObject;
        if (platformRoot is null)
        {
            return null;
        }

        if (!ReferenceEquals(platformRoot, _dialogNativeRoot))
        {
            ResetDialogNativeScrollViewer();
            _dialogNativeRoot = platformRoot;
        }

        if (_dialogNativeScrollViewer is null || refreshSelection)
        {
            ScrollViewer? candidate = FindLargestScrollViewer(platformRoot);
            if (!ReferenceEquals(candidate, _dialogNativeScrollViewer))
            {
                DetachDialogNativeScrollViewer();
                _dialogNativeScrollViewer = candidate;
                AttachDialogNativeScrollViewer(_dialogNativeScrollViewer);
            }
        }

        return _dialogNativeScrollViewer;
    }

    private void AttachDialogNativeScrollViewer(ScrollViewer? scroller)
    {
        if (scroller is null)
        {
            return;
        }

        scroller.ViewChanged += OnDialogNativeViewChanged;
        scroller.PointerPressed += OnDialogNativePointerPressed;
        scroller.PointerWheelChanged += OnDialogNativePointerWheelChanged;
        scroller.KeyDown += OnDialogNativeKeyDown;
        ObserveDialogScrollMetrics(scroller);
    }

    private void ResetDialogNativeScrollViewer()
    {
        DetachDialogNativeScrollViewer();
        _dialogNativeRoot = null;
    }

    private void DetachDialogNativeScrollViewer()
    {
        if (_dialogNativeScrollViewer is not null)
        {
            _dialogNativeScrollViewer.ViewChanged -= OnDialogNativeViewChanged;
            _dialogNativeScrollViewer.PointerPressed -= OnDialogNativePointerPressed;
            _dialogNativeScrollViewer.PointerWheelChanged -=
                OnDialogNativePointerWheelChanged;
            _dialogNativeScrollViewer.KeyDown -= OnDialogNativeKeyDown;
        }

        _dialogNativeScrollViewer = null;
        _dialogObservedVerticalOffset = -1;
        _dialogObservedScrollableHeight = -1;
    }

    private void OnDialogNativeViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs eventArgs)
    {
        if (sender is not ScrollViewer scroller
            || _dialogAutoScrollActive
            || _dialogScrollScheduled)
        {
            if (sender is ScrollViewer observedScroller)
            {
                ObserveDialogScrollMetrics(observedScroller);
            }

            return;
        }

        bool isAtBottom = IsNativeDialogAtBottom(scroller);
        if (ContinueFollowingAfterExtentGrowth(scroller, isAtBottom))
        {
            return;
        }

        _dialogScrollFollow.UpdatePosition(isAtBottom);
        UpdateDialogJumpToLatestButton();
    }

    private bool ContinueFollowingAfterExtentGrowth(
        ScrollViewer? scroller,
        bool isAtBottom)
    {
        if (scroller is null)
        {
            return false;
        }

        bool contentGrewBelowViewport = _dialogScrollFollow.IsFollowing
            && !isAtBottom
            && _dialogObservedVerticalOffset >= 0
            && _dialogObservedScrollableHeight >= 0
            && scroller.ScrollableHeight
                > _dialogObservedScrollableHeight + 0.5
            && scroller.VerticalOffset
                >= _dialogObservedVerticalOffset - 1;
        ObserveDialogScrollMetrics(scroller);
        if (!contentGrewBelowViewport)
        {
            return false;
        }

        QueueDialogScrollToBottom();
        return true;
    }

    private void ObserveDialogScrollMetrics(ScrollViewer? scroller)
    {
        if (scroller is null)
        {
            return;
        }

        _dialogObservedVerticalOffset = scroller.VerticalOffset;
        _dialogObservedScrollableHeight = scroller.ScrollableHeight;
    }

    private void OnDialogNativePointerPressed(
        object sender,
        PointerRoutedEventArgs eventArgs)
    {
        CancelDialogScroll();
    }

    private void OnDialogNativePointerWheelChanged(
        object sender,
        PointerRoutedEventArgs eventArgs)
    {
        CancelDialogScroll();
    }

    private void OnDialogNativeKeyDown(
        object sender,
        KeyRoutedEventArgs eventArgs)
    {
        if (eventArgs.Key == VirtualKey.End)
        {
            _dialogScrollFollow.FollowLatest();
            QueueDialogScrollToBottom();
            return;
        }

        if (eventArgs.Key is VirtualKey.Up
            or VirtualKey.PageUp
            or VirtualKey.Home)
        {
            CancelDialogScroll();
            _dialogScrollFollow.UpdatePosition(isAtBottom: false);
            UpdateDialogJumpToLatestButton();
        }
    }

    private void SynchronizeDialogMessageSubscriptions()
    {
        foreach (DialogMessageViewModel message in _observedDialogMessages
            .Where(message => !_viewModel.Dialog.Messages.Contains(message))
            .ToArray())
        {
            message.PropertyChanged -= OnDialogMessagePropertyChanged;
            _observedDialogMessages.Remove(message);
        }

        foreach (DialogMessageViewModel message in _viewModel.Dialog.Messages)
        {
            if (_observedDialogMessages.Add(message))
            {
                message.PropertyChanged += OnDialogMessagePropertyChanged;
            }
        }
    }

    private void UpdateDialogJumpToLatestButton()
    {
        DialogJumpToLatestButton.IsVisible = _viewModel.IsDialogMode
            && _viewModel.Dialog.Messages.Count > 0
            && !_dialogScrollFollow.IsFollowing;
        DialogJumpToLatestButton.Text = _dialogScrollFollow.HasUnseenTail
            ? "↓ New reply"
            : "↓ Latest";
    }

    private static bool AffectsDialogMessageHeight(string? propertyName)
    {
        return propertyName is nameof(DialogMessageViewModel.HasAudio)
            or nameof(DialogMessageViewModel.HasPronunciation)
            or nameof(DialogMessageViewModel.HasPronunciationWords)
            or nameof(DialogMessageViewModel.PhoneticTranscriptText)
            or nameof(DialogMessageViewModel.PronunciationSummary)
            or nameof(DialogMessageViewModel.IsWordLookupVisible)
            or nameof(DialogMessageViewModel.IsWordLookupLoading)
            or nameof(DialogMessageViewModel.WordPhoneticText)
            or nameof(DialogMessageViewModel.WordDefinitionText)
            or nameof(DialogMessageViewModel.WordPartOfSpeechText)
            or nameof(DialogMessageViewModel.WordLookupError);
    }

    private static bool IsNativeDialogAtBottom(
        ScrollViewer scroller,
        double tolerance = 32)
    {
        return scroller.ScrollableHeight <= 0
            || scroller.ScrollableHeight - scroller.VerticalOffset <= tolerance;
    }

    private static ScrollViewer? FindLargestScrollViewer(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        ScrollViewer? largest = root as ScrollViewer;
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            ScrollViewer? candidate = FindLargestScrollViewer(child);
            if (candidate is not null
                && (largest is null
                    || candidate.ScrollableHeight > largest.ScrollableHeight))
            {
                largest = candidate;
            }
        }

        return largest;
    }
}
#else
using System.Collections.Specialized;
using System.ComponentModel;
using Buddy.App.Services;
using Buddy.App.State;
using Buddy.App.ViewModels;
using Buddy.App.WinUI;

namespace Buddy.App;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly IDesktopTrayService _tray;
    private readonly DialogScrollFollowState _dialogScrollFollow = new();
    private readonly HashSet<DialogMessageViewModel> _observedDialogMessages = [];
    private bool _loaded;
    private bool _dialogScrollScheduled;
    private int _dialogScrollGeneration;

    public MainPage(
        MainViewModel viewModel,
        IDesktopTrayService tray)
    {
        StartupDiagnostics.Write("MainPage constructor before InitializeComponent");
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _tray = tray ?? throw new ArgumentNullException(nameof(tray));
        InitializeComponent();
        BindingContext = _viewModel;
        _viewModel.Dialog.Messages.CollectionChanged += OnDialogMessagesChanged;
        SynchronizeDialogMessageSubscriptions();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Runtime.PropertyChanged += OnRuntimePropertyChanged;
        _tray.Update(_viewModel.Runtime.Mode, _viewModel.Runtime.TrayToolTip);
        StartupDiagnostics.Write("MainPage constructor complete");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        StartupDiagnostics.Write("MainPage InitializeAsync starting");
        await _tray.InitializeAsync(_viewModel);
        await _viewModel.InitializeAsync();
        if (_viewModel.IsDialogMode
            && _viewModel.Dialog.Messages.Count > 0
            && _dialogScrollFollow.IsFollowing)
        {
            QueueDialogScrollToBottom();
        }

        StartupDiagnostics.Write("MainPage InitializeAsync complete");
    }

    private void OnDialogMessagesScrolled(
        object? sender,
        ItemsViewScrolledEventArgs eventArgs)
    {
        if (_dialogScrollScheduled)
        {
            return;
        }

        int count = _viewModel.Dialog.Messages.Count;
        bool isAtBottom = count == 0
            || eventArgs.LastVisibleItemIndex >= count - 1;
        _dialogScrollFollow.UpdatePosition(isAtBottom);
        UpdateDialogJumpToLatestButton();
    }

    private void OnDialogMessagesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs)
    {
        SynchronizeDialogMessageSubscriptions();
        int count = _viewModel.Dialog.Messages.Count;
        if (count == 0)
        {
            CancelDialogScroll();
            _dialogScrollFollow.Reset();
        }
        else if (_dialogScrollFollow.ShouldFollowAppend(eventArgs, count))
        {
            QueueDialogScrollToBottom();
        }

        UpdateDialogJumpToLatestButton();
    }

    private void OnDialogMessagePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (sender is not DialogMessageViewModel message
            || !AffectsDialogMessageHeight(eventArgs.PropertyName))
        {
            return;
        }

        int count = _viewModel.Dialog.Messages.Count;
        int index = _viewModel.Dialog.Messages.IndexOf(message);
        if (_dialogScrollFollow.ShouldFollowTailContentChange(index, count))
        {
            QueueDialogScrollToBottom();
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(MainViewModel.SelectedTabIndex)
            && eventArgs.PropertyName != nameof(MainViewModel.SelectedSpeakMode))
        {
            return;
        }

        if (_viewModel.IsDialogMode && _dialogScrollFollow.IsFollowing)
        {
            QueueDialogScrollToBottom();
        }
        else if (!_viewModel.IsDialogMode)
        {
            CancelDialogScroll();
        }

        UpdateDialogJumpToLatestButton();
    }

    private void OnDialogMessagesHandlerChanged(object? sender, EventArgs eventArgs)
    {
        if (_viewModel.IsDialogMode && _dialogScrollFollow.IsFollowing)
        {
            QueueDialogScrollToBottom();
        }
    }

    private void OnDialogJumpToLatestClicked(object? sender, EventArgs eventArgs)
    {
        _dialogScrollFollow.FollowLatest();
        UpdateDialogJumpToLatestButton();
        QueueDialogScrollToBottom();
    }

    private void OnRuntimePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(BuddyRuntimeState.Mode)
            or nameof(BuddyRuntimeState.TrayToolTip)
            or nameof(BuddyRuntimeState.RecordingElapsed))
        {
            Dispatcher.Dispatch(
                () => _tray.Update(
                    _viewModel.Runtime.Mode,
                    _viewModel.Runtime.TrayToolTip));
        }
    }

    private void QueueDialogScrollToBottom()
    {
        if (!_dialogScrollFollow.IsFollowing
            || !_viewModel.IsDialogMode
            || _viewModel.Dialog.Messages.Count == 0)
        {
            UpdateDialogJumpToLatestButton();
            return;
        }

        int generation = ++_dialogScrollGeneration;
        _dialogScrollScheduled = true;
        DialogMessages.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(45),
            () => ScrollDialogToBottom(generation, animate: _loaded));
    }

    private void ScrollDialogToBottom(int generation, bool animate)
    {
        if (generation != _dialogScrollGeneration
            || !_dialogScrollFollow.IsFollowing
            || !_viewModel.IsDialogMode
            || _viewModel.Dialog.Messages.Count == 0)
        {
            CompleteDialogScroll(generation);
            return;
        }

        DialogMessages.ScrollTo(
            _viewModel.Dialog.Messages.Count - 1,
            groupIndex: -1,
            ScrollToPosition.End,
            animate);
        DialogMessages.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(220),
            () =>
            {
                if (generation == _dialogScrollGeneration
                    && _dialogScrollFollow.IsFollowing
                    && _viewModel.Dialog.Messages.Count > 0)
                {
                    DialogMessages.ScrollTo(
                        _viewModel.Dialog.Messages.Count - 1,
                        groupIndex: -1,
                        ScrollToPosition.End,
                        animate: false);
                }

                CompleteDialogScroll(generation);
            });
    }

    private void CompleteDialogScroll(int generation)
    {
        if (generation != _dialogScrollGeneration)
        {
            return;
        }

        _dialogScrollScheduled = false;
        if (_dialogScrollFollow.IsFollowing)
        {
            _dialogScrollFollow.UpdatePosition(isAtBottom: true);
        }

        UpdateDialogJumpToLatestButton();
    }

    private void CancelDialogScroll()
    {
        _dialogScrollGeneration++;
        _dialogScrollScheduled = false;
    }

    private void SynchronizeDialogMessageSubscriptions()
    {
        foreach (DialogMessageViewModel message in _observedDialogMessages
            .Where(message => !_viewModel.Dialog.Messages.Contains(message))
            .ToArray())
        {
            message.PropertyChanged -= OnDialogMessagePropertyChanged;
            _observedDialogMessages.Remove(message);
        }

        foreach (DialogMessageViewModel message in _viewModel.Dialog.Messages)
        {
            if (_observedDialogMessages.Add(message))
            {
                message.PropertyChanged += OnDialogMessagePropertyChanged;
            }
        }
    }

    private void UpdateDialogJumpToLatestButton()
    {
        DialogJumpToLatestButton.IsVisible = _viewModel.IsDialogMode
            && _viewModel.Dialog.Messages.Count > 0
            && !_dialogScrollFollow.IsFollowing;
        DialogJumpToLatestButton.Text = _dialogScrollFollow.HasUnseenTail
            ? "↓ New reply"
            : "↓ Latest";
    }

    private static bool AffectsDialogMessageHeight(string? propertyName)
    {
        return propertyName is nameof(DialogMessageViewModel.HasAudio)
            or nameof(DialogMessageViewModel.HasPronunciation)
            or nameof(DialogMessageViewModel.HasPronunciationWords)
            or nameof(DialogMessageViewModel.PhoneticTranscriptText)
            or nameof(DialogMessageViewModel.PronunciationSummary)
            or nameof(DialogMessageViewModel.IsWordLookupVisible)
            or nameof(DialogMessageViewModel.IsWordLookupLoading)
            or nameof(DialogMessageViewModel.WordPhoneticText)
            or nameof(DialogMessageViewModel.WordDefinitionText)
            or nameof(DialogMessageViewModel.WordPartOfSpeechText)
            or nameof(DialogMessageViewModel.WordLookupError);
    }
}
#endif
