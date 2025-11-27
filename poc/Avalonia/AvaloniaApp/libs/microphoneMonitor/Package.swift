// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "microphoneMonitor",
    platforms: [
        .macOS(.v14)
    ],
    targets: [
        .executableTarget(
            name: "microphoneMonitor",
            path: "Sources"
        )
    ]
)