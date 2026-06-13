module.exports = {
    appId: "com.furrycord.app",
    productName: "Furrycord",
    copyright: "Copyright 2026 Furrycord",
    extraMetadata: {
        main: "dist/js/main.js",
        name: "furrycord"
    },
    asar: true,
    npmRebuild: false,
    files: [
        "package.json",
        "dist/js/**/*",
        "static/**/*",
        "!**/*.map",
        "!**/*.ts"
    ],
    extraResources: [
        {
            from: "dist/desktop.asar",
            to: "desktop.asar"
        },
        {
            from: "dist/furrycord.asar",
            to: "furrycord.asar"
        },
        {
            from: "ghost-server",
            to: "ghost-server",
            filter: [
                "server.js",
                "package.json"
            ]
        }
    ],
    directories: {
        output: "release",
        buildResources: "build"
    },
    win: {
        target: ["dir"],
        icon: "furrycord.ico",
        signAndEditExecutable: false
    },
    mac: {
        target: ["dir"],
        icon: "build/icon.icns",
        category: "public.app-category.social-networking",
        identity: null,
        hardenedRuntime: false,
        gatekeeperAssess: false
    }
};
