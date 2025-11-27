import Foundation
import AVFoundation
import OSLog
import CoreAudio
import AppKit

// Helper to write JSON to stdout in the required format: {"type": "log"|"event", "data": "..."}
// Write JSON to stdout
fileprivate func writeJSONToStdout(_ obj: Any) {
    if let data = try? JSONSerialization.data(withJSONObject: obj, options: []),
       let json = String(data: data, encoding: .utf8) {
        if let outData = (json + "\n").data(using: .utf8) {
            FileHandle.standardOutput.write(outData)
        }
    }
}

// Write JSON to stderr
fileprivate func writeJSONToStderr(_ obj: Any) {
    if let data = try? JSONSerialization.data(withJSONObject: obj, options: []),
       let json = String(data: data, encoding: .utf8) {
        if let outData = (json + "\n").data(using: .utf8) {
            FileHandle.standardError.write(outData)
        }
    }
}

// Log messages go to stderr with type "log"
fileprivate func jsonLog(_ message: String) {
    let obj: [String: Any] = ["type": "log", "data": message]
    writeJSONToStderr(obj)
}

// Events go to stdout with structured fields: {"type":"event","pid":<pid>,"process":"<name>","active":<true|false>}
fileprivate func jsonEventStructured(pid: Int32, process: String, active: Bool) {
    let obj: [String: Any] = ["type": "event", "pid": pid, "process": process, "active": active]
    writeJSONToStdout(obj)
}

class MicrophoneMonitor {
    
    // Store current processes using microphone
    private var currentMicUsers: Set<pid_t> = []
    
    // Log monitor for system logs
    private var logMonitor: LogMonitor?
    
    // Last detected mic client from logs
    private var lastMicClient: pid_t = 0
    
    // Audio listeners for devices
    private var audioListeners: [String: AudioObjectPropertyListenerBlock] = [:]
    
    // Event queue for handling events
    private let eventQueue = DispatchQueue(label: "com.micmonitor.eventQueue", attributes: .concurrent)
    
    init() {
        logMonitor = LogMonitor()
    }
    
    // Start monitoring
    func start() {
        jsonLog("Starting microphone monitoring...")
        
        // Start log monitor
        startLogMonitor()
        
        // Watch all audio input devices
        // `AVCaptureDevice.devices(for:)` was deprecated; use DiscoverySession
        let discovery: AVCaptureDevice.DiscoverySession
        if #available(macOS 14.0, *) {
            discovery = AVCaptureDevice.DiscoverySession(deviceTypes: [AVCaptureDevice.DeviceType.microphone], mediaType: .audio, position: .unspecified)
        } else {
            discovery = AVCaptureDevice.DiscoverySession(deviceTypes: [.builtInMicrophone], mediaType: .audio, position: .unspecified)
        }
        let audioDevices = discovery.devices
        for audioDevice in audioDevices {
            watchAudioDevice(audioDevice)
        }
        
    jsonLog("Microphone monitoring started!")
    }
    
    // Start monitoring system logs for microphone events
    private func startLogMonitor() {
        // Prefer the richer predicate and regex heuristics when available (macOS 13.3+).
        // macOS 14.0+ is a superset of 13.3 so we only need one availability guard.
        if #available(macOS 13.3, *) {
            if #available(macOS 14.0, *) {
                jsonLog("Using macOS 14.0+ log monitoring")
            } else {
                jsonLog("Using macOS 13.3+ log monitoring")
            }

            let micRegex = try? NSRegularExpression(pattern: "PID = (\\d+)", options: [])
            let pidRegexAlt = try? NSRegularExpression(pattern: "PID=(\\d+)", options: [])
            let pidRegexSemicolon = try? NSRegularExpression(pattern: "=\\s*(\\d+)\\s*;", options: [])

            // Broaden predicate to include coremedia, cmio and audio related subsystems
            let predicate = NSPredicate(format: "subsystem == 'com.apple.cmio' OR subsystem == 'com.apple.coremedia' OR subsystem CONTAINS 'audio' OR category == 'media'")

            logMonitor?.start(predicate: predicate) { [weak self] logEvent in
                guard let self = self else { return }

                // Microphone event detection
                // Try several heuristics to extract PID from a variety of log formats
                let msg = logEvent.composedMessage

                var foundPID: pid_t = 0

                if let match = micRegex?.firstMatch(in: msg, options: [], range: NSRange(location: 0, length: msg.count)) {
                    let pidString = (msg as NSString).substring(with: match.range(at: 1))
                    if let pid = pid_t(pidString), pid > 0 { foundPID = pid }
                }

                if foundPID == 0, let match = pidRegexAlt?.firstMatch(in: msg, options: [], range: NSRange(location: 0, length: msg.count)) {
                    let pidString = (msg as NSString).substring(with: match.range(at: 1))
                    if let pid = pid_t(pidString), pid > 0 { foundPID = pid }
                }

                if foundPID == 0, let match = pidRegexSemicolon?.firstMatch(in: msg, options: [], range: NSRange(location: 0, length: msg.count)) {
                    let pidString = (msg as NSString).substring(with: match.range(at: 1))
                    if let pid = pid_t(pidString), pid > 0 { foundPID = pid }
                }

                if foundPID > 0 {
                    self.lastMicClient = foundPID
                    let procName = self.getProcessName(forPID: foundPID)
                    jsonEventStructured(pid: foundPID, process: procName, active: true)
                } else {
                    // If we couldn't extract a PID from logs, still keep lastMicClient 0 and rely on fallback when audio becomes active
                    // Log for debugging
                    //jsonLog("[LogMonitor] no PID found in message: \(msg.prefix(200))")
                }
            }
        }
    }
    
    // Watch audio device for state changes
    private func watchAudioDevice(_ device: AVCaptureDevice) {
        guard let deviceID = getAudioObjectID(device) else {
            jsonLog("ERROR: Failed to get audio object ID for \(device.localizedName)")
            return
        }
        
        var propertyAddress = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyDeviceIsRunningSomewhere,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        
        let listenerBlock: AudioObjectPropertyListenerBlock = { [weak self] _, _ in
            guard let self = self else { return }
            
            let state = self.getMicState(device)
            
            if #available(macOS 13.3, *) {
                if state == true {
                    // Delay to allow log monitoring to capture PID
                    DispatchQueue.global().asyncAfter(deadline: .now() + 0.5) {
                        // If we don't have a PID from logs, try to guess the process (Safari / WebContent) as a fallback
                        var pid = self.lastMicClient
                        if pid == 0 {
                            if let fallback = self.findBrowserWebContentPID() {
                                pid = fallback
                                jsonLog("[Fallback] guessed PID: \(pid)")
                            }
                        }
                        self.handleMicrophoneActive(pid: pid, device: device)
                    }
                } else if state == false {
                    DispatchQueue.global().asyncAfter(deadline: .now() + 0.5) {
                        self.handleMicrophoneInactive(device: device)
                    }
                }
            }
        }
        
        let status = AudioObjectAddPropertyListenerBlock(
            deviceID,
            &propertyAddress,
            eventQueue,
            listenerBlock
        )
        
            if status == noErr {
            audioListeners[device.uniqueID] = listenerBlock
            jsonLog("Monitoring \(device.localizedName) for audio changes")
        } else {
            jsonLog("ERROR: Failed to add listener for \(device.localizedName)")
        }
    }
    
    // Handle microphone became active
    private func handleMicrophoneActive(pid: pid_t, device: AVCaptureDevice) {
    guard pid > 0, !currentMicUsers.contains(pid) else { return }

    currentMicUsers.insert(pid)

    let processName = getProcessName(forPID: pid)
    jsonEventStructured(pid: pid, process: processName, active: true)
    }
    
    // Handle microphone became inactive
    private func handleMicrophoneInactive(device: AVCaptureDevice) {
        // Check which processes stopped using mic
        let stillActive = getActiveAudioProcesses()
        let stopped = currentMicUsers.subtracting(stillActive)
        
        for pid in stopped {
            currentMicUsers.remove(pid)
            let processName = getProcessName(forPID: pid)
            jsonEventStructured(pid: pid, process: processName, active: false)
        }
    }
    
    // Get mic state for device
    private func getMicState(_ device: AVCaptureDevice) -> Bool {
        guard let deviceID = getAudioObjectID(device) else {
            return false
        }
        
        var propertyAddress = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyDeviceIsRunningSomewhere,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        
        var isRunning: UInt32 = 0
        var dataSize = UInt32(MemoryLayout<UInt32>.size)
        
        let status = AudioObjectGetPropertyData(
            deviceID,
            &propertyAddress,
            0,
            nil,
            &dataSize,
            &isRunning
        )
        
        return (status == noErr && isRunning != 0) ? true : false
    }
    
    // Get audio object ID for AVCaptureDevice
    private func getAudioObjectID(_ device: AVCaptureDevice) -> AudioObjectID? {
        var propertyAddress = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        
        var dataSize: UInt32 = 0
        var status = AudioObjectGetPropertyDataSize(
            AudioObjectID(kAudioObjectSystemObject),
            &propertyAddress,
            0,
            nil,
            &dataSize
        )
        
        guard status == noErr else { return nil }
        
        let deviceCount = Int(dataSize) / MemoryLayout<AudioObjectID>.size
        var audioDevices = [AudioObjectID](repeating: 0, count: deviceCount)
        
        status = AudioObjectGetPropertyData(
            AudioObjectID(kAudioObjectSystemObject),
            &propertyAddress,
            0,
            nil,
            &dataSize,
            &audioDevices
        )
        
        guard status == noErr else { return nil }
        
        // Try to match by UID
        for audioDeviceID in audioDevices {
            var uidPropertyAddress = AudioObjectPropertyAddress(
                mSelector: kAudioDevicePropertyDeviceUID,
                mScope: kAudioObjectPropertyScopeGlobal,
                mElement: kAudioObjectPropertyElementMain
            )
            
            // Read UID safely into an optional CFString and compare
            var uid: CFString? = nil
            var uidSize = UInt32(MemoryLayout<CFString?>.size)

            let status = withUnsafeMutablePointer(to: &uid) { uidPtr -> OSStatus in
                uidPtr.withMemoryRebound(to: UInt8.self, capacity: Int(uidSize)) { rawPtr in
                    return AudioObjectGetPropertyData(
                        audioDeviceID,
                        &uidPropertyAddress,
                        0,
                        nil,
                        &uidSize,
                        rawPtr
                    )
                }
            }

            if status == noErr, let uid = uid as String?, uid == device.uniqueID {
                return audioDeviceID
            }
        }
        
        return nil
    }
    
    // Get currently active audio processes
    private func getActiveAudioProcesses() -> Set<pid_t> {
        var activePIDs: Set<pid_t> = []
        
        if lastMicClient > 0 {
            activePIDs.insert(lastMicClient)
        }

        // If we don't have a PID from logs, try a more direct detection using CoreAudio
        if activePIDs.isEmpty {
            if detectMicrophoneUsage() {
                // If microphone is in use but we don't have a PID, try to enumerate likely apps
                let apps = getRunningApplicationsWithMicrophoneAccess()
                for app in apps {
                    if let pid = app.processIdentifier as pid_t? {
                        activePIDs.insert(pid)
                    }
                }

                // Fallback to browser WebContent heuristic if still empty
                if activePIDs.isEmpty, let fallback = findBrowserWebContentPID() {
                    activePIDs.insert(fallback)
                }
            } else {
                // No microphone usage detected - keep empty
            }
        }
        
        return activePIDs
    }

    // New: Directly detect if any microphone device is active using CoreAudio
    func detectMicrophoneUsage() -> Bool {
        // Get all microphone devices
        let microphoneDevices = getMicrophoneDevices()

        for device in microphoneDevices {
            guard let deviceID = getAudioObjectID(device) else { continue }

            var propertyAddress = AudioObjectPropertyAddress(
                mSelector: kAudioDevicePropertyDeviceIsRunningSomewhere,
                mScope: kAudioObjectPropertyScopeGlobal,
                mElement: kAudioObjectPropertyElementMain
            )

            var isRunning: UInt32 = 0
            var propertySize = UInt32(MemoryLayout<UInt32>.size)

            let status = AudioObjectGetPropertyData(
                deviceID,
                &propertyAddress,
                0,
                nil,
                &propertySize,
                &isRunning
            )

            if status == noErr && isRunning != 0 {
                jsonLog("Microphone \(device.localizedName) is active")
                return true
            }
        }
        return false
    }

    // New: enumerate AVCapture audio devices
    func getMicrophoneDevices() -> [AVCaptureDevice] {
        // .builtInMicrophone was deprecated in macOS 14. Use .microphone when available.
        if #available(macOS 14.0, *) {
            let discovery = AVCaptureDevice.DiscoverySession(deviceTypes: [AVCaptureDevice.DeviceType.microphone], mediaType: .audio, position: .unspecified)
            return discovery.devices
        } else {
            let discovery = AVCaptureDevice.DiscoverySession(deviceTypes: [.builtInMicrophone], mediaType: .audio, position: .unspecified)
            return discovery.devices
        }
    }

    // New: list running applications that likely have mic access
    func getRunningApplicationsWithMicrophoneAccess() -> [NSRunningApplication] {
        let runningApps = NSWorkspace.shared.runningApplications
        
        // Filter for regular applications (not system processes)
        return runningApps.filter { app in
            app.activationPolicy == .regular && 
            hasMicrophonePermission(for: app)
        }
    }

    private func hasMicrophonePermission(for app: NSRunningApplication) -> Bool {
    // This is a simplified check - in practice, you'd need to query TCC database
    // or use other methods to determine microphone permissions
        guard let bundleIdentifier = app.bundleIdentifier else { return false }
        
        // Common apps that typically have microphone access
        let commonMicApps = [
            "com.zoom.xos",
            "com.microsoft.teams",
            "com.apple.facetime",
            "com.skype.skype",
            "com.discord.discord"
        ]
        
        return commonMicApps.contains(bundleIdentifier)
    }
    
    // Get process name for PID
    private func getProcessName(forPID pid: pid_t) -> String {
        var pathBuffer = [CChar](repeating: 0, count: 4096)
        let pathLength = proc_pidpath(pid, &pathBuffer, UInt32(4096))
        
        if pathLength > 0 {
            let path = String(cString: pathBuffer)
            return (path as NSString).lastPathComponent
        }

        return "Unknown"
    }

    // Attempt to locate a likely browser/WebContent process hosting Meet (Safari, Chrome)
    // This is a heuristic fallback when logs don't provide a PID.
    private func findBrowserWebContentPID() -> pid_t? {
        // Check common process names for browsers and their helper/WebContent processes
        let candidates = ["WebContent", "Safari", "Google Chrome", "Google Chrome Helper", "chrome", "Chromium"]

    // Use ps to list processes and match names (lightweight, avoids additional frameworks)
        let task = Process()
        task.launchPath = "/bin/ps"
        task.arguments = ["-axo", "pid=,comm="]

        let pipe = Pipe()
        task.standardOutput = pipe
        do {
            try task.run()
        } catch {
            return nil
        }

        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        guard let out = String(data: data, encoding: .utf8) else { return nil }

        let lines = out.split(separator: "\n")
        for line in lines {
            let parts = line.trimmingCharacters(in: .whitespaces).split(separator: " ", maxSplits: 1, omittingEmptySubsequences: true)
            if parts.count == 2 {
                let pidStr = String(parts[0])
                let comm = String(parts[1])

                for cand in candidates {
                    if comm.contains(cand) {
                        if let pid = pid_t(pidStr) {
                            return pid
                        }
                    }
                }
            }
        }

        return nil
    }
}

// Simple LogMonitor wrapper (you'll need to implement based on OSLog)
class LogMonitor {
    private var logStore: OSLogStore?
    
    @available(macOS 13.3, *)
    func start(predicate: NSPredicate, callback: @escaping (OSLogEntryLog) -> Void) {
        DispatchQueue.global(qos: .background).async {
            do {
                let logStore = try OSLogStore(scope: .currentProcessIdentifier)
                let position = logStore.position(timeIntervalSinceLatestBoot: 0)
                
                let entries = try logStore.getEntries(at: position, matching: predicate)
                
                for entry in entries {
                    if let logEntry = entry as? OSLogEntryLog {
                        callback(logEntry)
                    }
                }
            } catch {
                jsonLog("Error accessing logs: \(error)")
            }
        }
    }
}

// Main entry point
let monitor = MicrophoneMonitor()
monitor.start()

// Keep running
RunLoop.main.run()