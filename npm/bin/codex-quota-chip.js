#!/usr/bin/env node
// Launches the app that install.js unpacked, passing the arguments straight through, and exits
// with the same code. Run without arguments it starts the taskbar overlay.
'use strict';

const { existsSync } = require('node:fs');
const { spawnSync } = require('node:child_process');
const path = require('node:path');

const exe = path.join(__dirname, '..', 'vendor', 'CodexQuotaChip.exe');
if (!existsSync(exe)) {
  console.error('codex-quota-chip: the app is not unpacked yet - run "npm rebuild codex-quota-chip".');
  process.exit(1);
}

const result = spawnSync(exe, process.argv.slice(2), { stdio: 'inherit' });
process.exit(result.status === null ? 1 : result.status);
