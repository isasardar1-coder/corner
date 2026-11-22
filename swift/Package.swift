// swift-tools-version: 5.9
// Corner for macOS: the platform-neutral model (CornerCore), its checks (CornerChecks) and the app (CornerApp,
// product "Corner": AppKit + SwiftUI). See README.md.
import PackageDescription

let package = Package(
    name: "Corner",
    platforms: [.macOS(.v12)],
    products: [
        .library(name: "CornerCore", targets: ["CornerCore"]),
        .executable(name: "CornerChecks", targets: ["CornerChecks"]),
        .executable(name: "Corner", targets: ["CornerApp"]),
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
        .executableTarget(
            name: "CornerApp",
            dependencies: ["CornerCore"],
            path: "Sources/CornerApp",
            resources: [
                .copy("Resources/Gilroy-Medium.ttf"),
                .copy("Resources/Gilroy-SemiBold.ttf"),
                .copy("Resources/Gilroy-SemiBoldItalic.ttf"),
                .copy("Resources/ping.wav"),
            ]
        ),
    ],
    swiftLanguageVersions: [.v5]
)
