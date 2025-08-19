# Screen Sharing Invisibility Implementation

* Status: proposed
* Deciders: [list everyone involved in the decision]
* Date: 2025-08-19

## Context and Problem Statement

A key requirement for our application is that it must not be visible during screen sharing sessions using tools like Microsoft Teams, Zoom, or similar applications. We need to determine the most effective and reliable approach to implement this invisibility across different platforms.

## Decision Drivers

* Effectiveness at remaining invisible during screen sharing
* Cross-platform compatibility (Windows and macOS required, Linux optional)
* Stability and reliability of the approach
* Technical feasibility within our chosen framework (Tauri)
* Maintainability as screen sharing applications evolve
* User experience implications
* Performance impact

## Considered Options

* Window Layering with Special Flags
* Custom Graphics Layer Approach
* Screen Capture API Detection and Hiding
* Virtual Display Exclusion

## Decision Outcome

Chosen option: "Window Layering with Special Flags", because it provides the most reliable method across platforms, leverages existing OS capabilities, and can be implemented efficiently within our Tauri framework using Rust for platform-specific code.

### Positive Consequences

* High reliability across different screen sharing applications
* Leverages established OS-level mechanisms
* Can be implemented without excessive complexity
* Minimal performance impact
* Consistent behavior regardless of screen sharing tool used
* Clear implementation path through Tauri's Rust backend

### Negative Consequences

* Requires platform-specific code for Windows and macOS
* May need updates as OS versions evolve
* Potential for screen sharing applications to change how they capture in the future
* Limited control over third-party application behavior
* Some edge cases may exist with certain screen sharing tools

## Pros and Cons of the Options

### Window Layering with Special Flags

* Good, because it uses established OS-level window flags that are respected by most capture software
* Good, because it has proven effectiveness on both Windows (WS_EX_TRANSPARENT + WDA_EXCLUDEFROMCAPTURE) and macOS (kCGWindowSharingNone)
* Good, because it leverages existing system mechanisms rather than workarounds
* Good, because it provides consistent behavior across different screen sharing tools
* Good, because it can be implemented via Tauri's Rust layer with platform-specific code
* Bad, because it requires maintaining separate implementations for Windows and macOS
* Bad, because it may require updates if OS windowing systems change
* Bad, because some newer screen sharing tools might use different capture methods

### Custom Graphics Layer Approach

* Good, because it potentially offers more control over rendering
* Good, because it might allow for more advanced visual effects while remaining invisible
* Good, because it could provide a unified cross-platform approach
* Bad, because it has higher complexity and development effort
* Bad, because it may be less reliable than using OS-provided mechanisms
* Bad, because it could have higher performance overhead
* Bad, because it would likely require more maintenance as platforms evolve

### Screen Capture API Detection and Hiding

* Good, because it actively responds to screen sharing being initiated
* Good, because it could adapt behavior based on what's being shared
* Good, because it might offer more flexibility in how the application responds
* Bad, because screen capture detection APIs are limited and inconsistent across platforms
* Bad, because it relies on detection which may not always be reliable
* Bad, because it introduces complexity in application state management
* Bad, because it may create noticeable UI changes when sharing begins/ends

### Virtual Display Exclusion

* Good, because it works at a lower level than window management
* Good, because it might be more difficult for future screen sharing tools to bypass
* Good, because it could potentially be more consistent across different sharing tools
* Bad, because it requires more invasive system integration
* Bad, because it has limited support on some platforms
* Bad, because it may require elevated permissions
* Bad, because it could interfere with legitimate display functionality

## Links

* [Windows Window Attributes Documentation](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)
* [macOS Window Level Documentation](https://developer.apple.com/documentation/appkit/nswindow/level)
* [Tauri Window API Documentation](https://tauri.app/v1/api/js/window/)
* [Microsoft Teams Screen Sharing Documentation](https://docs.microsoft.com/en-us/microsoftteams/sharing-content-in-teams)
* [Zoom Screen Sharing Best Practices](https://support.zoom.us/hc/en-us/articles/201362153-Sharing-your-screen-or-desktop-on-Zoom)
