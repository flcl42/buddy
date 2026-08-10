using System.Windows.Input;
using Buddy.App.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace Buddy.App.Controls;

public sealed class AudioTransportView : ContentView
{
    public static readonly BindableProperty StateProperty = BindableProperty.Create(
        nameof(State),
        typeof(AudioTransportState),
        typeof(AudioTransportView),
        AudioTransportState.Idle,
        propertyChanged: OnPresentationChanged);

    public static readonly BindableProperty SubjectProperty = BindableProperty.Create(
        nameof(Subject),
        typeof(string),
        typeof(AudioTransportView),
        "audio",
        propertyChanged: OnPresentationChanged);

    public static readonly BindableProperty PlayCommandProperty =
        BindableProperty.Create(
            nameof(PlayCommand),
            typeof(ICommand),
            typeof(AudioTransportView),
            propertyChanged: OnCommandsChanged);

    public static readonly BindableProperty PauseCommandProperty =
        BindableProperty.Create(
            nameof(PauseCommand),
            typeof(ICommand),
            typeof(AudioTransportView),
            propertyChanged: OnCommandsChanged);

    public static readonly BindableProperty StopCommandProperty =
        BindableProperty.Create(
            nameof(StopCommand),
            typeof(ICommand),
            typeof(AudioTransportView),
            propertyChanged: OnCommandsChanged);

    public static readonly BindableProperty ResetCommandProperty =
        BindableProperty.Create(
            nameof(ResetCommand),
            typeof(ICommand),
            typeof(AudioTransportView),
            propertyChanged: OnCommandsChanged);

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(
            nameof(CommandParameter),
            typeof(object),
            typeof(AudioTransportView),
            propertyChanged: OnCommandsChanged);

    private readonly Button _playButton;
    private readonly Button _pauseButton;
    private readonly Button _stopButton;
    private readonly Button _resetButton;
    private readonly BoxView _divider;

    public AudioTransportView()
    {
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Center;

        _playButton = CreateButton("AudioTransportPlayButton");
        _pauseButton = CreateButton("AudioTransportPauseButton");
        _stopButton = CreateButton("AudioTransportStopButton");
        _resetButton = CreateButton("AudioTransportResetButton");
        _divider = new BoxView
        {
            BackgroundColor = Color.FromArgb("#D7DAE8"),
            HeightRequest = 18,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 1,
        };

        HorizontalStackLayout buttons = new()
        {
            Spacing = 0,
            Children =
            {
                _playButton,
                _stopButton,
                _divider,
                _pauseButton,
                _resetButton,
            },
        };
        Content = new Border
        {
            Padding = 0,
            BackgroundColor = Color.FromArgb("#F3F4FC"),
            Stroke = new SolidColorBrush(Color.FromArgb("#D7DAE8")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(8),
            },
            Content = buttons,
        };

        UpdateCommands();
        UpdatePresentation();
    }

    public AudioTransportState State
    {
        get => (AudioTransportState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Subject
    {
        get => (string)GetValue(SubjectProperty);
        set => SetValue(SubjectProperty, value);
    }

    public ICommand? PlayCommand
    {
        get => (ICommand?)GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public ICommand? PauseCommand
    {
        get => (ICommand?)GetValue(PauseCommandProperty);
        set => SetValue(PauseCommandProperty, value);
    }

    public ICommand? StopCommand
    {
        get => (ICommand?)GetValue(StopCommandProperty);
        set => SetValue(StopCommandProperty, value);
    }

    public ICommand? ResetCommand
    {
        get => (ICommand?)GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private static Button CreateButton(string automationId)
    {
        return new Button
        {
            AutomationId = automationId,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 0,
            FontFamily = "Segoe UI Semibold",
            FontSize = 13,
            HeightRequest = 30,
            MinimumHeightRequest = 30,
            MinimumWidthRequest = 32,
            Padding = 0,
            TextColor = Color.FromArgb("#4D50C6"),
            WidthRequest = 32,
        };
    }

    private static void OnPresentationChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        ((AudioTransportView)bindable).UpdatePresentation();
    }

    private static void OnCommandsChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        ((AudioTransportView)bindable).UpdateCommands();
    }

    private void UpdateCommands()
    {
        _playButton.Command = PlayCommand;
        _pauseButton.Command = PauseCommand;
        _stopButton.Command = StopCommand;
        _resetButton.Command = ResetCommand;
        _playButton.CommandParameter = CommandParameter;
        _pauseButton.CommandParameter = CommandParameter;
        _stopButton.CommandParameter = CommandParameter;
        _resetButton.CommandParameter = CommandParameter;
    }

    private void UpdatePresentation()
    {
        bool isPreparing = State == AudioTransportState.Preparing;
        bool isPlaying = State == AudioTransportState.Playing;
        bool isPaused = State == AudioTransportState.Paused;
        string subject = string.IsNullOrWhiteSpace(Subject)
            ? "audio"
            : Subject.Trim();

        _playButton.IsVisible = !isPlaying;
        _playButton.IsEnabled = !isPreparing;
        _playButton.Text = isPreparing ? "…" : "▶";
        _stopButton.IsVisible = isPlaying;
        _pauseButton.IsVisible = isPlaying;
        _resetButton.IsVisible = isPaused;
        _divider.IsVisible = isPlaying || isPaused;

        SetDescription(
            _playButton,
            isPreparing
                ? $"Preparing {subject}"
                : isPaused
                    ? $"Resume {subject}"
                    : $"Play {subject}");
        _stopButton.Text = "■";
        SetDescription(_stopButton, $"Stop {subject} and reset to the beginning");
        _pauseButton.Text = "Ⅱ";
        SetDescription(_pauseButton, $"Pause {subject}");
        _resetButton.Text = "↺";
        SetDescription(_resetButton, $"Restart {subject} from the beginning");
        SemanticProperties.SetDescription(
            this,
            isPreparing
                ? $"Preparing {subject}"
                : isPlaying
                    ? $"{subject} is playing"
                    : isPaused
                        ? $"{subject} is paused"
                        : $"Play {subject}");
    }

    private static void SetDescription(Button button, string description)
    {
        SemanticProperties.SetDescription(button, description);
        ToolTipProperties.SetText(button, description);
    }
}
