using System;
using System.Threading.Tasks;
using Avalonia;
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
	StopSessionOnStopButton,
	TestDialog

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

	// Display the dialog as a transient corner notification (non-modal).
	// Returns when the window is closed; caller can inspect Result.
	public async Task ShowAsCornerNotificationAsync(Window? owner = null, int margin = 40)
	{
		// Use Topmost so it appears above other windows like a notification
		Topmost = true;

		// Remove window decorations to look like a toast
		CanResize = false;
		
		// Override the WindowStartupLocation to Manual so we control positioning
		WindowStartupLocation = WindowStartupLocation.Manual;

		// Measure size after layout to position correctly
		if (owner != null)
			Owner = owner;

		// Show non-modal first
		Show();
		
		// Wait a moment for the window to be fully rendered and measured
		await Task.Delay(150);
		
		try
		{
			// Now that window is shown, we can get accurate measurements
			var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
			if (screen != null)
			{
				var working = screen.WorkingArea;
				var scaling = screen.Scaling;
				
				System.Diagnostics.Debug.WriteLine($"=== POSITIONING DEBUG ===");
				System.Diagnostics.Debug.WriteLine($"Screen Bounds: {screen.Bounds}");
				System.Diagnostics.Debug.WriteLine($"Working Area: {working}");
				System.Diagnostics.Debug.WriteLine($"Window Bounds (logical): {Bounds}");
				System.Diagnostics.Debug.WriteLine($"Current Position: {Position}");
				System.Diagnostics.Debug.WriteLine($"DesktopScaling: {scaling}");
				
				// Get the window size in logical pixels and convert to physical pixels
				var windowWidthLogical = Bounds.Width;
				var windowHeightLogical = Bounds.Height;
				
				// Convert to physical pixels by multiplying by scaling factor
				var windowWidthPhysical = windowWidthLogical * scaling;
				var windowHeightPhysical = windowHeightLogical * scaling;
				
				System.Diagnostics.Debug.WriteLine($"Window Size - Logical: {windowWidthLogical}x{windowHeightLogical}");
				System.Diagnostics.Debug.WriteLine($"Window Size - Physical: {windowWidthPhysical}x{windowHeightPhysical}");
				
				// Working area is in physical pixels, so use physical window size
				var x = (int)(working.Right - windowWidthPhysical - margin);
				var y = (int)(working.Bottom - windowHeightPhysical - margin);
				
				System.Diagnostics.Debug.WriteLine($"Target Position (physical pixels): X={x}, Y={y}");
				
				// Set position in physical pixels
				Position = new PixelPoint(x, y);
				await Task.Delay(50);
				Position = new PixelPoint(x, y);
				
				System.Diagnostics.Debug.WriteLine($"Final Position: {Position}");
				System.Diagnostics.Debug.WriteLine($"=========================");
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Positioning failed: {ex.Message}");
		}
		
		// Wait until window is closed
		while (IsVisible)
		{
			await Task.Delay(100);
		}
	}

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


