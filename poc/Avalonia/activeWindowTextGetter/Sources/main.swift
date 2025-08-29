import AppKit
import Foundation

func getAllText(from element: AXUIElement, allText: inout [String]) {
    var children: AnyObject?
    let result = AXUIElementCopyAttributeValue(element, kAXChildrenAttribute as CFString, &children)

    if result == .success, let children = children as? [AXUIElement] {
        for child in children {
            var textValue: AnyObject?
            let textResult = AXUIElementCopyAttributeValue(child, kAXValueAttribute as CFString, &textValue)
            if textResult == .success, let text = textValue as? String, !text.isEmpty {
                allText.append(text)
            }
            getAllText(from: child, allText: &allText)
        }
    }
}


// Check for accessibility permissions first.
/*guard AXIsProcessTrusted() else {
    let errorData = "Accessibility permissions are not granted. Please grant them in System Settings.\n".data(using: .utf8)!
    FileHandle.standardError.write(errorData)
    exit(1)
}*/

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

var allText = [String]()
getAllText(from: windowElement, allText: &allText)

if allText.isEmpty {
    let errorData = "Could not find any text in the active window.\n".data(using: .utf8)!
    FileHandle.standardError.write(errorData)
    exit(1)
} else {
    print(allText.joined(separator: "\n"))
}