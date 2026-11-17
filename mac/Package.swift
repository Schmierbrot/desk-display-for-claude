// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "ClaudeDisplay",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(name: "ClaudeDisplay", path: "Sources/ClaudeDisplay")
    ]
)
