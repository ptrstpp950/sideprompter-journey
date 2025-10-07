using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AvaloniaApp;

public enum ConfirmDialogResult
{
	Yes,
	YesDontAskAgain,
	No
}

public partial class ConfirmDialog : Window
{
	public ConfirmDialog()
	{
		InitializeComponent();
	}

	public string Message
	{
		get => MessageText.Text ?? string.Empty;
		set => MessageText.Text = value;
	}

	public ConfirmDialogResult Result { get; private set; } = ConfirmDialogResult.No;

	private void YesButton_OnClick(object? sender, RoutedEventArgs e)
	{
		Result = ConfirmDialogResult.Yes;
		Close();
	}

	private void YesDontAskButton_OnClick(object? sender, RoutedEventArgs e)
	{
		Result = ConfirmDialogResult.YesDontAskAgain;
		Close();
	}

	private void NoButton_OnClick(object? sender, RoutedEventArgs e)
	{
		Result = ConfirmDialogResult.No;
		Close();
	}
}

// Usage example:
// var dlg = new ConfirmDialog { Message = "Proceed?" };
// await dlg.ShowDialog(ownerWindow);
// var res = dlg.Result;

