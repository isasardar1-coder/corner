// swift-tools-version: 5.9
// Corner for macOS: the platform-neutral model (CornerCore) and its checks (CornerChecks).
// An app target (CornerApp, depending on CornerCore) is meant to be added here later.
import PackageDescription

let package = Package(
    name: "Corner",
    platforms: [.macOS(.v12)],
    products: [
        .library(name: "CornerCore", targets: ["CornerCore"]),
        .executable(name: "CornerChecks", targets: ["CornerChecks"]),
        // .executable(name: "CornerApp", targets: ["CornerApp"]),
    ],
    targets: [
        .target(
            name: "CornerCore",
            path: "Sources/CornerCore"
        ),
        .executableTarget(
            name: "CornerChecks",
            dependencies: ["CornerCore"],
            path: "Sources/CornerChecks"
        ),
        // .executableTarget(name: "CornerApp", dependencies: ["CornerCore"], path: "Sources/CornerApp"),
    ],
    swiftLanguageVersions: [.v5]
)
