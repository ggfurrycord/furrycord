/*
 * Vencord, a modification for Discord's desktop app
 * Copyright (c) 2022 Vendicated and contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

import { app } from "electron";
import { existsSync, mkdirSync, readdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from "original-fs";
import { basename, dirname, join } from "path";

function isNewer($new: string, old: string) {
    const newParts = $new.slice(4).split(".").map(Number);
    const oldParts = old.slice(4).split(".").map(Number);

    for (let i = 0; i < oldParts.length; i++) {
        if (newParts[i] > oldParts[i]) return true;
        if (newParts[i] < oldParts[i]) return false;
    }
    return false;
}

/** Same loader logic as the C# installer — survives Discord host updates on all channels. */
function buildLoaderJs() {
    return `// Furrycord auto-repatch after Discord host update
"use strict";
const fs = require("fs");
const path = require("path");
const primary = path.join(process.env.LOCALAPPDATA || process.env.APPDATA, "Furrycord", "dist", "patcher.js");
const exeDir = path.dirname(process.execPath);
const fallback = path.join(exeDir, "resources", "dist", "patcher.js");
const fallback2 = path.join(exeDir, "dist", "patcher.js");
const patcherPath = fs.existsSync(primary) ? primary : fs.existsSync(fallback) ? fallback : fallback2;
if (!fs.existsSync(patcherPath)) throw new Error("[Furrycord] patcher.js not found after host update. Run the Furrycord installer to repair.");
require(patcherPath);
`;
}

function patchLatest() {
    try {
        const currentAppPath = dirname(process.execPath);
        const currentVersion = basename(currentAppPath);
        const discordPath = join(currentAppPath, "..");

        const latestVersion = readdirSync(discordPath)
            .filter(name => name.startsWith("app-") && statSync(join(discordPath, name)).isDirectory())
            .reduce((prev, curr) => isNewer(curr, prev) ? curr : prev, currentVersion as string);

        if (latestVersion === currentVersion) return;

        const resources = join(discordPath, latestVersion, "resources");
        const appAsar = join(resources, "app.asar");
        const backup = join(resources, "_app.asar");
        const appDir = join(resources, "app");

        // Already patched in the new version folder
        if (existsSync(appDir)) {
            const pkg = join(appDir, "package.json");
            if (existsSync(pkg) && readFileSync(pkg, "utf8").includes("\"furrycord\"")) return;
        }

        if (!existsSync(appAsar) || statSync(appAsar).isDirectory()) return;

        console.info(`[Furrycord] Discord updated to ${latestVersion}. Re-injecting Furrycord...`);

        if (existsSync(appDir)) rmSync(appDir, { recursive: true, force: true });
        if (existsSync(backup)) rmSync(backup, { force: true });

        renameSync(appAsar, backup);
        mkdirSync(appDir);
        writeFileSync(join(appDir, "package.json"), JSON.stringify({ name: "furrycord", main: "index.js" }));
        writeFileSync(join(appDir, "index.js"), buildLoaderJs());
    } catch (err) {
        console.error("[Furrycord] Failed to repatch latest host update", err);
    }
}

// Discord's Win32 updater calls app.quit() on restart and opens the new version on will-quit
app.on("before-quit", patchLatest);
