import AppKit
import Foundation

func findUrlsInText(_ text: String) -> [String] {
    // Simple URL detection regex
    let detector = try? NSDataDetector(types: NSTextCheckingResult.CheckingType.link.rawValue)
    var urls: [String] = []
    
    if let detector = detector {
        let matches = detector.matches(in: text, options: [], range: NSRange(location: 0, length: text.utf16.count))
        for match in matches {
            if let url = match.url?.absoluteString {
                urls.append(url)
            }
        }
    }
    
    return urls
}

func getAllText(from element: AXUIElement, allText: inout [String], urls: inout [String]) {
    var children: AnyObject?
    let result = AXUIElementCopyAttributeValue(element, kAXChildrenAttribute as CFString, &children)

    if result == .success, let children = children as? [AXUIElement] {
        for child in children {
            // Get text value
            var textValue: AnyObject?
            let textResult = AXUIElementCopyAttributeValue(child, kAXValueAttribute as CFString, &textValue)
            if textResult == .success, let text = textValue as? String, !text.isEmpty {
                allText.append(text)
                // Check for URLs
                let detectedUrls = findUrlsInText(text)
                urls.append(contentsOf: detectedUrls)
            }
            
            // Also check title attribute (common for links)
            var titleValue: AnyObject?
            let titleResult = AXUIElementCopyAttributeValue(child, kAXTitleAttribute as CFString, &titleValue)
            if titleResult == .success, let title = titleValue as? String, !title.isEmpty {
                allText.append(title)
                // Check for URLs
                let detectedUrls = findUrlsInText(title)
                urls.append(contentsOf: detectedUrls)
            }
            
            // Process children recursively
            getAllText(from: child, allText: &allText, urls: &urls)
        }
    }
}

// Check for accessibility permissions
/*guard AXIsProcessTrusted() else {
    print("""
    {
      "status": "error",
      "message": "Accessibility permissions are not granted. Please grant them in System Settings."
    }
    """)
    exit(1)
}*/

guard let frontmostApp = NSWorkspace.shared.frontmostApplication else {
    print("""
    {
      "status": "error",
      "message": "Could not get frontmost application."
    }
    """)
    exit(1)
}

let pid = frontmostApp.processIdentifier
let appName = frontmostApp.localizedName ?? "Unknown"
let bundleId = frontmostApp.bundleIdentifier ?? "Unknown"
let appElement = AXUIElementCreateApplication(pid)

var focusedWindow: AnyObject?
let windowResult = AXUIElementCopyAttributeValue(appElement, kAXFocusedWindowAttribute as CFString, &focusedWindow)

if windowResult != .success {
    print("""
    {
      "status": "error",
      "message": "Could not get focused window.",
      "application": "\(appName)",
      "bundleId": "\(bundleId)"
    }
    """)
    exit(1)
}

let windowElement = focusedWindow as! AXUIElement

// Get window title
var windowTitle = "Unknown"
var windowTitleObj: AnyObject?
let titleResult = AXUIElementCopyAttributeValue(windowElement, kAXTitleAttribute as CFString, &windowTitleObj)
if titleResult == .success, let title = windowTitleObj as? String {
    windowTitle = title
}

// Initialize result data
var output: [String: Any] = [
    "application": appName,
    "bundleId": bundleId,
    "title": windowTitle
]

// Check if browser, try to get URL directly
if bundleId.contains("safari") || bundleId.contains("chrome") || bundleId.contains("firefox") || bundleId.contains("edge") {
    var url: AnyObject?
    let urlResult = AXUIElementCopyAttributeValue(windowElement, kAXURLAttribute as CFString, &url)
    if urlResult == .success, let urlString = url as? String {
        output["url"] = urlString
    }
}

// Check if it's a productivity app and try to get document path
let productivityApps = [
    "com.microsoft.Word", "com.microsoft.Excel", "com.microsoft.Powerpoint", 
    "com.apple.iWork.Pages", "com.apple.iWork.Numbers", "com.apple.iWork.Keynote",
    "com.apple.Notes", "md.obsidian", "com.google.docs", "com.google.sheets",
    "com.apple.TextEdit"
]

let isProductivityApp = productivityApps.contains { bundleId.contains($0.lowercased()) }

if isProductivityApp || bundleId.contains("word") || bundleId.contains("excel") || bundleId.contains("notes") || 
   bundleId.contains("pages") || bundleId.contains("numbers") || bundleId.contains("text") {
    
    // Try to get document path through accessibility API
    var documentPath: AnyObject?
    let documentResult = AXUIElementCopyAttributeValue(windowElement, kAXDocumentAttribute as CFString, &documentPath)
    
    if documentResult == .success, let docPath = documentPath as? String {
        output["documentPath"] = docPath
    } else {
        // Try to extract file path from the window title
        // Many apps include the filename in the window title, often with " - " separating app name from file name
        let titleComponents = windowTitle.components(separatedBy: " - ")
        if titleComponents.count > 1 {
            // The last component might be the filename
            let possibleFileName = titleComponents.last!
            if possibleFileName.contains(".") {
                output["possibleFileName"] = possibleFileName
            }
        }
    }
}

// Get window content to detect URLs
var allText: [String] = []
var detectedUrls: [String] = []
getAllText(from: windowElement, allText: &allText, urls: &detectedUrls)

// Add text content
let textContent = allText.joined(separator: " ")
output["text"] = textContent

// Add detected URLs if not already found and this is a browser
if output["url"] == nil && !detectedUrls.isEmpty {
    // Find the most likely URL (e.g., first one that starts with http/https)
    let mainUrls = detectedUrls.filter { $0.hasPrefix("http") }
    if !mainUrls.isEmpty {
        output["url"] = mainUrls.first!
    } else {
        output["url"] = detectedUrls.first!
    }
}

// Convert to JSON and print
if let jsonData = try? JSONSerialization.data(withJSONObject: output, options: [.prettyPrinted]),
   let jsonString = String(data: jsonData, encoding: .utf8) {
    print(jsonString)
} else {
    print("""
    {
      "status": "error", 
      "message": "Failed to convert output to JSON",
      "application": "\(appName)",
      "bundleId": "\(bundleId)"
    }
    """)
    exit(1)
}