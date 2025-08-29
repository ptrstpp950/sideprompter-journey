
import AppKit
import Foundation

// Check for accessibility permissions first.
guard AXIsProcessTrusted() else {
    let errorData = "Accessibility permissions are not granted. Please grant them in System Settings.\n".data(using: .utf8)!
    FileHandle.standardError.write(errorData)
    exit(1)
}

guard let frontmostApp = NSWorkspace.shared.frontmostApplication else {
    let errorData = "Could not get frontmost application.\n".data(using: .utf8)!
    FileHandle.standardError.write(errorData)
    exit(1)
}

let pid = frontmostApp.processIdentifier
let appName = frontmostApp.localizedName ?? "Unknown"
let errorString = "Frontmost application: \(appName) (PID: \(pid))\n"
let errorData = errorString.data(using: .utf8)!
FileHandle.standardError.write(errorData)

let appElement = AXUIElementCreateApplication(pid)

var focusedWindow: AnyObject?
let windowResult = AXUIElementCopyAttributeValue(appElement, kAXFocusedWindowAttribute as CFString, &focusedWindow)

if windowResult != .success {
    let errorData = "Could not get focused window.\n".data(using: .utf8)!
    FileHandle.standardError.write(errorData)
    exit(1)
}

let windowElement = focusedWindow as! AXUIElement

var focusedElement: AnyObject?
let focusedResult = AXUIElementCopyAttributeValue(windowElement, kAXFocusedUIElementAttribute as CFString, &focusedElement)

if focusedResult != .success {
    let errorData = "Could not get focused UI element.\n".data(using: .utf8)!
    FileHandle.standardError.write(errorData)
    exit(1)
}

let uiElement = focusedElement as! AXUIElement

var textValue: AnyObject?
let textResult = AXUIElementCopyAttributeValue(uiElement, kAXValueAttribute as CFString, &textValue)

if textResult == .success, let text = textValue as? String {
    print(text)
} else {
    let errorData = "Could not get text from the focused element.\n".data(using: .utf8)!
    FileHandle.standardError.write(errorData)
    exit(1)
}
