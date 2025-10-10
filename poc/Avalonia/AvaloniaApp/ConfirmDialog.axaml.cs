using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AvaloniaApp;

public enum ConfirmDialogResult
{
	Yes,
	No
}

public enum ConfirmDialogType
{
	StartTranscriptionWhenMicrophoneIsActive,
	StopTranscriptionWhenMicrophoneIsInActive,
	StopSessionOnStopButton

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
	public bool RememberAnswer { get; private set; } = false;

	private void YesButton_OnClick(object? sender, RoutedEventArgs e)
	{
		Result = ConfirmDialogResult.Yes;
		RememberAnswer = RememberCheckBox.IsChecked == true;
		Close();
	}

	private void NoButton_OnClick(object? sender, RoutedEventArgs e)
	{
		Result = ConfirmDialogResult.No;
		RememberAnswer = RememberCheckBox.IsChecked == true;
		Close();
	}
}

// Usage example:
// var dlg = new ConfirmDialog { Message = "Proceed?" };
// await dlg.ShowDialog(ownerWindow);
// var res = dlg.Result;


