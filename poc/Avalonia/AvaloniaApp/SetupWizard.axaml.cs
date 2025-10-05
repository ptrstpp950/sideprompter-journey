using Avalonia.Controls;
using AvaloniaApp.Settings;

namespace AvaloniaApp;

public partial class SetupWizard : Window
{
    private readonly SetupWizardViewModel _vm;

    public SetupWizard() : this(SettingsService.Load(), false) { }
    public SetupWizard(AppSettings settings, bool isSettingsMode, bool updateIsReady = false)
    {
        InitializeComponent();
        _vm = new SetupWizardViewModel(settings, isSettingsMode);
        DataContext = _vm;
        _vm.UpdateIsReady = updateIsReady;
        _vm.RequestClose += (_, _) => Close();
    }
}
