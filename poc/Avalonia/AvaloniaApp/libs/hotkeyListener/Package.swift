// swift-tools-version:5.5
import PackageDescription

let package = Package(
    name: "hotkeyListener",
    platforms: [
        .macOS(.v10_15)
    ],
    dependencies: [
        .package(url: "https://github.com/soffes/HotKey", from: "0.2.1")
    ],
    targets: [
        .executableTarget(
            name: "hotkeyListener",
            dependencies: ["HotKey"],
            path: "Sources"
        )
    ]
)