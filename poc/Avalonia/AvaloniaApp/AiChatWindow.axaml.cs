using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaApp.ViewModel;
using System;
using System.Threading.Tasks;
#if WINDOWS
using System.Windows.Forms;
#endif

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
#if WINDOWS
                // Use a dedicated STA thread for WinForms clipboard calls.
                // Task.Run uses a thread-pool thread (MTA) which causes a
                // ThreadStateException when calling Ole functions.
                var tcs = new TaskCompletionSource<bool>();
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        System.Windows.Forms.Clipboard.SetText(txt);
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                await tcs.Task;
#else
                var top = TopLevel.GetTopLevel(this);
                if (top?.Clipboard != null)
                    await top.Clipboard.SetTextAsync(txt);
#endif
            }
            catch { }
        }
    }
    // Sending is handled by AiOnlyChatViewModel.AskCommand via binding.
}
