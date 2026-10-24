using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Kite.App.Interop;
using Kite.App.Services;
using Kite.App.ViewModels;
using Kite.Core.Storage;

namespace Kite.App.Views;

/// <summary>the floating search bar.</summary>
public partial class LauncherWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private readonly Func<LauncherSettings> _settings;
    private int _dialogDepth;

    internal LauncherWindow(LauncherViewModel viewModel, Func<LauncherSettings> settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;

        Deactivated += OnDeactivated;
        PreviewKeyDown += OnPreviewKeyDown;
        SearchBox.TextChanged += (_, _) => UpdatePlaceholder();
        ResultsList.SelectionChanged += OnSelectionChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdatePlaceholder();
    }

    /// <summary>raised when the user asks for the settings window.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>true when animations are allowed by both kite and windows.</summary>
    private bool AnimationsEnabled => _settings().Animations && SystemParameters.ClientAreaAnimation;

    public void ShowLauncher()
    {
        var monitor = _settings().Monitor;
        MonitorService.PlaceWindow(this, monitor);

        if (!IsVisible)
        {
            Show();
        }

        // a second pass fixes the position after windows applies the monitor's scaling
        Dispatcher.InvokeAsync(() => MonitorService.PlaceWindow(this, monitor), DispatcherPriority.Loaded);

        Activate();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        FocusSearchBox();
        PlayShowAnimation();
        _viewModel.UpdateAppStatus();
    }

    public void HideLauncher()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    /// <summary>
    /// keep the launcher open while one of its own dialogs is showing.
    /// dispose the result when the dialog closes.
    /// </summary>
    public IDisposable KeepOpen()
    {
        _dialogDepth++;
        return new DialogScope(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // closing would destroy the window, so just hide it unless the app is quitting
        if (Application.Current is App { IsShuttingDown: false })
        {
            e.Cancel = true;
            HideLauncher();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Deactivated -= OnDeactivated;
        PreviewKeyDown -= OnPreviewKeyDown;
        ResultsList.SelectionChanged -= OnSelectionChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();

        // focus again once the window is really active
        Dispatcher.InvokeAsync(
            () =>
            {
                Keyboard.Focus(SearchBox);
                SearchBox.SelectAll();
            },
            DispatcherPriority.Input);
    }

    private void PlayShowAnimation()
    {
        if (!AnimationsEnabled)
        {
            Root.BeginAnimation(OpacityProperty, null);
            RootOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            Root.Opacity = 1;
            RootOffset.Y = 0;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)) { EasingFunction = ease });
        RootOffset.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
    }

    private void UpdateBusyBar()
    {
        if (!_viewModel.IsBusy)
        {
            BusyBarOffset.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            return;
        }

        if (!AnimationsEnabled)
        {
            // a still bar when motion is turned off
            BusyBar.Width = double.NaN;
            BusyBar.HorizontalAlignment = HorizontalAlignment.Stretch;
            BusyBar.Opacity = 0.6;
            return;
        }

        BusyBar.Width = 160;
        BusyBar.HorizontalAlignment = HorizontalAlignment.Left;
        BusyBar.Opacity = 1;
        var trackWidth = Math.Max(BusyTrack.ActualWidth, 600);
        var sweep = new DoubleAnimation(-160, trackWidth, TimeSpan.FromMilliseconds(1100))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        BusyBarOffset.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, sweep);
    }

    private void UpdatePlaceholder()
    {
        Placeholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_dialogDepth == 0)
        {
            HideLauncher();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Escape:
                HideLauncher();
                e.Handled = true;
                break;
            case Key.Down:
                _viewModel.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                _viewModel.SelectLast();
                e.Handled = true;
                break;
            case Key.PageUp:
                _viewModel.SelectFirst();
                e.Handled = true;
                break;
            case Key.Home when ctrl:
                _viewModel.SelectFirst();
                e.Handled = true;
                break;
            case Key.End when ctrl:
                _viewModel.SelectLast();
                e.Handled = true;
                break;
            case Key.Enter when SettingsButton.IsKeyboardFocused:
                // let the gear button handle its own enter key
                break;
            case Key.Enter:
                ActivateSelected();
                e.Handled = true;
                break;
        }
    }

    private void ActivateSelected()
    {
        if (_viewModel.ActivateSelected())
        {
            HideLauncher();
            _viewModel.QueryText = string.Empty;
        }
    }

    private void OnResultClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(ResultsList, source) is not ListBoxItem container)
        {
            return;
        }

        var index = ResultsList.ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0)
        {
            return;
        }

        _viewModel.SelectedIndex = index;
        ActivateSelected();
        e.Handled = true;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        HideLauncher();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not ResultItemViewModel item)
        {
            return;
        }

        ResultsList.ScrollIntoView(item);
        Announce(item.AutomationName);
    }

    /// <summary>tell screen readers about the highlighted result, since focus stays in the search box.</summary>
    private void Announce(string text)
    {
        Announcer.Text = text;
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            var peer = UIElementAutomationPeer.FromElement(Announcer) ?? UIElementAutomationPeer.CreatePeerForElement(Announcer);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherViewModel.IsBusy))
        {
            UpdateBusyBar();
        }
    }

    private sealed class DialogScope(LauncherWindow owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner._dialogDepth = Math.Max(0, owner._dialogDepth - 1);
            }
        }
    }
}
