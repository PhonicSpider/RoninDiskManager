using MahApps.Metro.Controls;
using RoninDiskManager.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RoninDiskManager;

public partial class MainWindow : MetroWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // Global keyboard shortcuts
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => Vm?.OpenInExplorerCommand.Execute(null)),
            new KeyGesture(Key.E, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => Vm?.CopyPathCommand.Execute(null)),
            new KeyGesture(Key.C, ModifierKeys.Control | ModifierKeys.Shift)));
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void DiskTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedNode = e.NewValue as DiskNodeViewModel;
    }

    private void ConsoleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.ScrollToEnd();
    }

    // Minimal ICommand wrapper for InputBinding — no need for a full RelayCommand here
    private sealed class RelayCommand(Action execute) : ICommand
    {
#pragma warning disable CS0067  // event is required by ICommand but never raised (always executable)
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? _) => true;
        public void Execute(object? _) => execute();
    }
}
