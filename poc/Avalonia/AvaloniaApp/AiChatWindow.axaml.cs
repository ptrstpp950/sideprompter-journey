using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaApp.ViewModel;
using System;
using System.Threading.Tasks;

namespace AvaloniaApp;

public partial class AiChatWindow : Window
{
    public AiChatWindow()
    {
        InitializeComponent();
    }

    public AiChatWindow(AiOnlyChatViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void CopyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Button btn && btn.CommandParameter is string txt)
        {
            try
            {
                // Use Avalonia's cross-platform clipboard API for all platforms.
                var top = TopLevel.GetTopLevel(this);
                if (top?.Clipboard != null)
                {
                    await top.Clipboard.SetTextAsync(txt);
                }
            }
            catch { }
        }
    }
    // Sending is handled by AiOnlyChatViewModel.AskCommand via binding.
}
