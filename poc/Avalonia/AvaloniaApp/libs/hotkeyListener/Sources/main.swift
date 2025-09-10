import AppKit
import HotKey
import ApplicationServices
import Darwin

/// Print to stderr for visibility to any caller/process manager.
func logError(_ message: String) {
    fputs("\(message)\n", stderr)
}

/// Check and (optionally) prompt for Accessibility permissions which are commonly required
/// for global hotkey listeners on macOS.
func checkAccessibilityPromptIfNeeded() -> Bool {
    // If already trusted, fine. If not, prompt the user.
    if AXIsProcessTrusted() {
        return true
    }

    logError("Accessibility permissions are not granted. Prompting the user to grant permission...")
    let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
    let trustedNow = AXIsProcessTrustedWithOptions(options)
    return trustedNow
}

/// Basic signal handlers to allow a clean exit and a helpful message.
func installSignalHandlers() {
    signal(SIGINT) { _ in
        logError("Received SIGINT — shutting down hotkey listener.")
        exit(0)
    }
    signal(SIGTERM) { _ in
        logError("Received SIGTERM — shutting down hotkey listener.")
        exit(0)
    }
}

/// Install an Objective-C uncaught exception handler so crashes surface a helpful message.
func installUncaughtExceptionHandler() {
    NSSetUncaughtExceptionHandler { exception in
        logError("Uncaught exception: \(exception) — terminating.")
        // Try to flush stderr then exit with non-zero code.
        fflush(stderr)
        exit(1)
    }
}

// Entry
installSignalHandlers()
installUncaughtExceptionHandler()

logError("Hotkey listener starting. Press CMD+? or OPTION+?")

let cmdAndQuestion = HotKey(key: .slash, modifiers: [.command])
let optAndQuestion = HotKey(key: .slash, modifiers: [.option])

logError("Hotkey listener started")

var counter = 0

cmdAndQuestion.keyDownHandler = {
    logError("[Event] CMD+?")
    counter += 1
}

optAndQuestion.keyDownHandler = {
    logError("[Event] OPTION+?")
    counter += 1
}

// Run the Cocoa application run loop. This call blocks until the app exits.
NSApplication.shared.run()
