#!/usr/bin/env node
// Downloads the Windows build from the GitHub release for this package's version, checks it
// against the sha256 pinned in package.json, and unpacks it next to this package.
//
// This is a wrapper, not a JavaScript library: the app is a C#/WPF executable. The download is
// verified before anything is extracted, and npm has no unzip of its own, so the extraction goes
// through PowerShell's Expand-Archive (this package only installs on Windows anyway).
'use strict';

const { createHash } = require('node:crypto');
const { execFileSync } = require('node:child_process');
const { createWriteStream, existsSync, mkdirSync, rmSync, statSync } = require('node:fs');
const path = require('node:path');
const https = require('node:https');

const packageRoot = path.join(__dirname, '..');
const pkg = require(path.join(packageRoot, 'package.json'));
const version = pkg.version;
const asset = `CodexQuotaChip-${version}-win-x64.zip`;
const url = `https://github.com/easerlee/codex-quota-chip/releases/download/v${version}/${asset}`;
const expected = pkg.codexQuotaChip && pkg.codexQuotaChip.sha256;
const vendor = path.join(packageRoot, 'vendor');
const archive = path.join(vendor, asset);

function fail(message) {
  console.error(`codex-quota-chip: ${message}`);
  console.error('You can also install the app itself from https://github.com/easerlee/codex-quota-chip/releases');
  process.exit(1);
}

function download(from, to, redirects = 0) {
  return new Promise((resolve, reject) => {
    if (redirects > 5) {
      reject(new Error('too many redirects'));
      return;
    }

    https
      .get(from, { headers: { 'user-agent': 'codex-quota-chip-npm' } }, (response) => {
        const status = response.statusCode || 0;
        if (status >= 300 && status < 400 && response.headers.location) {
          response.resume();
          download(response.headers.location, to, redirects + 1).then(resolve, reject);
          return;
        }
        if (status !== 200) {
          response.resume();
          reject(new Error(`${status} while downloading ${from}`));
          return;
        }

        const file = createWriteStream(to);
        response.pipe(file);
        file.on('finish', () => file.close(resolve));
        file.on('error', reject);
      })
      .on('error', reject);
  });
}

function sha256(file) {
  const hash = createHash('sha256');
  hash.update(require('node:fs').readFileSync(file));
  return hash.digest('hex');
}

async function main() {
  if (process.platform !== 'win32') {
    fail('this package only installs on Windows');
  }

  if (!expected || expected.length !== 64) {
    fail('package.json carries no sha256 for the download; refusing to fetch it');
  }

  mkdirSync(vendor, { recursive: true });
  console.log(`codex-quota-chip: downloading ${asset} (${version}) ...`);

  try {
    await download(url, archive);
  } catch (error) {
    fail(`download failed: ${error.message}`);
  }

  const actual = sha256(archive);
  if (actual !== expected) {
    rmSync(archive, { force: true });
    fail(`checksum mismatch: expected ${expected}, got ${actual}`);
  }

  console.log('codex-quota-chip: checksum ok, unpacking ...');
  try {
    execFileSync(
      'powershell',
      ['-NoProfile', '-NonInteractive', '-Command', `Expand-Archive -LiteralPath '${archive}' -DestinationPath '${vendor}' -Force`],
      { stdio: 'inherit' }
    );
  } catch (error) {
    fail(`could not unpack the archive: ${error.message}`);
  }

  const exe = path.join(vendor, 'CodexQuotaChip.exe');
  if (!existsSync(exe) || statSync(exe).size === 0) {
    fail('the archive did not contain CodexQuotaChip.exe');
  }

  rmSync(archive, { force: true });
  console.log(`codex-quota-chip: ready - ${exe}`);
  console.log('run "codex-quota-chip" to start it, or launch the exe directly.');
}

main().catch((error) => fail(error.message));
