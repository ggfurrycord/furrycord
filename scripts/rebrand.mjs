import fs from "fs";
import path from "path";

const ROOT = path.resolve(import.meta.dirname, "..");
const SKIP_DIRS = new Set(["node_modules", ".git", "dist", "release"]);
const TEXT_EXT = /\.(ts|tsx|js|mjs|mts|cjs|json|html|css|md|ps1|bat|yml|cs|csproj|manifest)$/;

function walk(dir, files = []) {
    for (const name of fs.readdirSync(dir)) {
        const full = path.join(dir, name);
        if (SKIP_DIRS.has(name)) continue;
        const stat = fs.statSync(full);
        if (stat.isDirectory()) walk(full, files);
        else if (TEXT_EXT.test(name) || name === "Makefile") files.push(full);
    }
    return files;
}

function replaceContent(content) {
    const pairs = [
        ["@nightcordplugins", "@furrycordplugins"],
        ["@Nightcord", "@Furrycord"],
        ["nightcordplugins", "furrycordplugins"],
        ["Nightcord", "Furrycord"],
        ["NIGHTCORD", "FURRYCORD"],
        ["nightcord", "furrycord"],
    ];
    let out = content;
    for (const [from, to] of pairs) {
        out = out.split(from).join(to);
    }
    return out;
}

function renameIfExists(from, to) {
    if (fs.existsSync(from)) {
        fs.renameSync(from, to);
        console.log(`Renamed: ${path.relative(ROOT, from)} -> ${path.relative(ROOT, to)}`);
    }
}

// 1. Text replacements
let updated = 0;
for (const file of walk(ROOT)) {
    if (file.endsWith("rebrand.mjs")) continue;
    const original = fs.readFileSync(file, "utf8");
    const next = replaceContent(original);
    if (next !== original) {
        fs.writeFileSync(file, next, "utf8");
        updated++;
    }
}
console.log(`Updated ${updated} text files`);

// 2. Plugin subdir renames (before parent dir rename)
renameIfExists(path.join(ROOT, "src/nightcordplugins/nightcordUpdater"), path.join(ROOT, "src/nightcordplugins/furrycordUpdater"));
renameIfExists(path.join(ROOT, "src/nightcordplugins/nightcordAI"), path.join(ROOT, "src/nightcordplugins/furrycordAI"));
renameIfExists(path.join(ROOT, "src/nightcordplugins/autoTranslateNightcord"), path.join(ROOT, "src/nightcordplugins/autoTranslateFurrycord"));
renameIfExists(path.join(ROOT, "src/nightcordplugins"), path.join(ROOT, "src/furrycordplugins"));
renameIfExists(path.join(ROOT, "src/nightcord"), path.join(ROOT, "src/furrycord"));

// 3. File renames
const fileRenames = [
    ["nightcord-index.js", "furrycord-index.js"],
    ["nightcord-preload.js", "furrycord-preload.js"],
    ["nightcord-install.ps1", "furrycord-install.ps1"],
    ["nightcord-install.bat", "furrycord-install.bat"],
    ["nightcord-uninstall.ps1", "furrycord-uninstall.ps1"],
    ["nightcord-uninstall.bat", "furrycord-uninstall.bat"],
    ["nightcord.ico", "furrycord.ico"],
    ["src/main/nightcordTray.ts", "src/main/furrycordTray.ts"],
    ["installer-src/NightcordInstaller.csproj", "installer-src/FurrycordInstaller.csproj"],
];

for (const [from, to] of fileRenames) {
    renameIfExists(path.join(ROOT, from), path.join(ROOT, to));
}

// 4. Fix package.json metadata
const pkgPath = path.join(ROOT, "package.json");
const pkg = JSON.parse(fs.readFileSync(pkgPath, "utf8"));
pkg.name = "furrycord";
pkg.homepage = "https://github.com/furrycord/furrycord";
pkg.bugs = { url: "https://github.com/furrycord/furrycord/issues" };
pkg.repository = { type: "git", url: "git+https://github.com/furrycord/furrycord.git" };
fs.writeFileSync(pkgPath, JSON.stringify(pkg, null, 4) + "\n", "utf8");

// 5. Update README for furrycord branding
const readmePath = path.join(ROOT, "README.md");
let readme = fs.readFileSync(readmePath, "utf8");
readme = readme.replace(/https:\/\/Furrycord\.su[^\s)"]*/g, "");
readme = readme.replace(/https:\/\/discord\.gg\/Furrycord/g, "");
readme = readme.replace(/git clone https:\/\/github\.com\/20ch\/Furrycord\.git/g, "git clone https://github.com/furrycord/furrycord.git");
fs.writeFileSync(readmePath, readme, "utf8");

// Verify
const remaining = [];
for (const file of walk(ROOT)) {
    if (file.endsWith("rebrand.mjs")) continue;
    const content = fs.readFileSync(file, "utf8");
    if (/nightcord/i.test(content)) remaining.push(path.relative(ROOT, file));
}
if (remaining.length) {
    console.warn("Remaining nightcord references:", remaining.slice(0, 20));
} else {
    console.log("Rebrand complete — no nightcord references remain");
}
