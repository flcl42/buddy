using System.Windows.Input;
using Buddy.App.ViewModels;
using Microsoft.Maui.Graphics;

namespace Buddy.App.Controls;

public sealed class WaveformView : GraphicsView
{
    public static readonly BindableProperty SamplesProperty =
        BindableProperty.Create(
            nameof(Samples),
            typeof(IReadOnlyList<float>),
            typeof(WaveformView),
            Array.Empty<float>(),
            propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(
            nameof(Progress),
            typeof(double),
            typeof(WaveformView),
            0d,
            propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty SeekCommandProperty =
        BindableProperty.Create(
            nameof(SeekCommand),
            typeof(ICommand),
            typeof(WaveformView));

    public static readonly BindableProperty SeekCommandParameterProperty =
        BindableProperty.Create(
            nameof(SeekCommandParameter),
            typeof(object),
            typeof(WaveformView));

    public WaveformView()
    {
        Drawable = new WaveformDrawable(this);
        StartInteraction += OnStartInteraction;
    }

    public IReadOnlyList<float> Samples
    {
        get => (IReadOnlyList<float>)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    public object? SeekCommandParameter
    {
        get => GetValue(SeekCommandParameterProperty);
        set => SetValue(SeekCommandParameterProperty, value);
    }

    private void OnStartInteraction(
        object? sender,
        TouchEventArgs eventArgs)
    {
        if (eventArgs.Touches.Length == 0
            || Width <= 0
            || SeekCommandParameter is not RecordingCardViewModel recording)
        {
            return;
        }

        WaveformSeekRequest request = new(
            recording,
            Math.Clamp(eventArgs.Touches[0].X / Width, 0, 1));
        if (SeekCommand?.CanExecute(request) == true)
        {
            SeekCommand.Execute(request);
        }
    }

    private static void OnVisualPropertyChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        ((WaveformView)bindable).Invalidate();
    }

    private sealed class WaveformDrawable : IDrawable
    {
        private static readonly Color PlayedColor = Color.FromArgb("#5B5CE2");
        private static readonly Color RemainingColor = Color.FromArgb("#C8CBD7");
        private static readonly Color CenterColor = Color.FromArgb("#E6E8EF");
        private static readonly Color PlayheadColor = Color.FromArgb("#3436B5");
        private readonly WaveformView _owner;

        public WaveformDrawable(WaveformView owner)
        {
            _owner = owner;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            IReadOnlyList<float> samples = _owner.Samples;
            if (samples.Count == 0 || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            {
                return;
            }

            float centerY = dirtyRect.Center.Y;
            canvas.StrokeColor = CenterColor;
            canvas.StrokeSize = 1;
            canvas.DrawLine(
                dirtyRect.Left,
                centerY,
                dirtyRect.Right,
                centerY);

            double progress = Math.Clamp(_owner.Progress, 0, 1);
            float slotWidth = dirtyRect.Width / samples.Count;
            float barWidth = Math.Max(1, slotWidth * 0.58f);
            float maximumBarHeight = Math.Max(4, dirtyRect.Height - 5);
            float radius = Math.Min(2, barWidth / 2);
            for (int index = 0; index < samples.Count; index++)
            {
                float level = float.IsFinite(samples[index])
                    ? Math.Clamp(samples[index], 0, 1)
                    : 0;
                float barHeight = Math.Max(3, maximumBarHeight * level);
                float x = dirtyRect.Left
                    + index * slotWidth
                    + (slotWidth - barWidth) / 2;
                float y = centerY - barHeight / 2;
                double centerFraction = (index + 0.5d) / samples.Count;
                canvas.FillColor = centerFraction <= progress
                    ? PlayedColor
                    : RemainingColor;
                canvas.FillRoundedRectangle(
                    x,
                    y,
                    barWidth,
                    barHeight,
                    radius);
            }

            if (progress > 0)
            {
                float playheadX = dirtyRect.Left
                    + (float)(dirtyRect.Width * progress);
                canvas.StrokeColor = PlayheadColor;
                canvas.StrokeSize = 1.5f;
                canvas.DrawLine(
                    playheadX,
                    dirtyRect.Top + 1,
                    playheadX,
                    dirtyRect.Bottom - 1);
            }
        }
    }
}
