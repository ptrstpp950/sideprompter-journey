# Active Window Text Getter

A simple macOS utility that extracts essential information from the active window for LLM analysis.

## Features

- Gets application name and bundle ID of the active window
- Captures window title
- Detects URLs in browsers and other applications
- Retrieves document paths for productivity applications (Word, Excel, Notes, etc.)
- Extracts text content from the window
- Outputs data in JSON format

## Output Format

The tool outputs a JSON object with the following structure:

```json
{
  "application": "Application Name",
  "bundleId": "com.example.app",
  "title": "Window Title",
  "url": "https://example.com",  // Only present if a URL is detected
  "documentPath": "/path/to/document.docx",  // Only present for productivity apps
  "possibleFileName": "document.docx",  // Fallback when document path isn't available
  "text": "Text content from the window"
}
```

## Requirements

- macOS 10.15 or later
- Accessibility permissions (will prompt if not granted)

## Usage

Simply run the executable. It will output JSON data about the currently active window.

```bash
swift run
```

## For LLM Analysis

This tool provides the most essential information an LLM would need to understand the user's current context:

1. **Application context**: The app name and bundle ID help identify what program the user is working with
2. **Content context**: The window title and text content provide insight into what the user is viewing
3. **Web context**: If the user is browsing the web, the URL provides critical context about what site they're on
4. **Document context**: For productivity apps, the document path helps identify what file the user is working on

By focusing only on the most important information, the output remains concise and easy to process.
