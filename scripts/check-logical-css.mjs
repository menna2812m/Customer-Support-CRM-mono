#!/usr/bin/env node
/**
 * Direction-neutral styles (spec FR-037, LR-002).
 *
 * Physical direction properties are the reason RTL support usually needs a per-screen exception.
 * Logical properties mirror automatically, so the rule is: never write left or right in a style.
 */
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, dirname, resolve, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const roots = [join(repoRoot, 'frontend', 'projects')];

const FORBIDDEN = [
  { pattern: /(?<![\w-])(margin|padding|border)-(left|right)\s*:/g, hint: 'use the -inline-start / -inline-end variant' },
  { pattern: /(?<![\w-])(left|right)\s*:\s*(?!auto)/g, hint: 'use inset-inline-start / inset-inline-end' },
  { pattern: /text-align\s*:\s*(left|right)/g, hint: 'use text-align: start / end' },
  { pattern: /float\s*:\s*(left|right)/g, hint: 'use float: inline-start / inline-end' },
];

const violations = [];

function walk(directory) {
  for (const entry of readdirSync(directory)) {
    if (entry === 'node_modules' || entry === 'dist') {
      continue;
    }

    const full = join(directory, entry);

    if (statSync(full).isDirectory()) {
      walk(full);
      continue;
    }

    if (!['.scss', '.css'].includes(extname(full))) {
      continue;
    }

    const content = readFileSync(full, 'utf8');

    for (const { pattern, hint } of FORBIDDEN) {
      for (const match of content.matchAll(pattern)) {
        const line = content.slice(0, match.index).split('\n').length;
        violations.push(`${full}:${line} "${match[0].trim()}" - ${hint}`);
      }
    }
  }
}

roots.forEach(walk);

if (violations.length > 0) {
  console.error('Physical direction properties found. Styles must mirror without per-screen exceptions:');
  violations.forEach((violation) => console.error(`  - ${violation}`));
  process.exit(1);
}

console.log('logical css: no physical direction properties found.');
