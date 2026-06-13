/*
 * Vencord, a Discord client mod
 * Copyright (c) 2026 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

// Disabled: Discord now reuses the same predicate object after the return value.
// Replacing the return target with `true` makes Discord call `true.supports(...)`
// when opening settings.
