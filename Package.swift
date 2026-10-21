// swift-tools-version:5.10
import PackageDescription

let package = Package(
    name: "Recess",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "Recess",
            path: "Sources/Recess",
            linkerSettings: [
                .linkedFramework("Carbon"),
                .linkedFramework("WebKit"),
                .linkedFramework("EventKit"),
                .linkedFramework("ServiceManagement"),
                .linkedFramework("UserNotifications"),
            ]
        ),
        .testTarget(
            name: "RecessTests",
            dependencies: ["Recess"],
            path: "Tests/RecessTests"
        ),
    ]
)
