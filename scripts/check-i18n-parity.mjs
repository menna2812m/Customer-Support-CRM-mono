#!/usr/bin/env node
/**
 * Translation key parity (spec FR-039, LR-001).
 *
 * A key present in one language and missing in the other is invisible until a user in that
 * language hits the screen. Comparing the key sets turns that into a build failure instead.
 */
import { readFileSync, readdirSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const i18nDir = join(repoRoot, 'frontend', 'projects', 'crm-web', 'public', 'assets', 'i18n');

function flatten(value, prefix = '') {
  return Object.entries(value).flatMap(([key, entry]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    return entry && typeof entry === 'object' && !Array.isArray(entry)
      ? flatten(entry, path)
      : [path];
  });
}

const files = readdirSync(i18nDir).filter((name) => name.endsWith('.json'));

if (files.length < 2) {
  console.error(`i18n parity: expected at least two language files in ${i18nDir}, found ${files.length}.`);
  process.exit(1);
}

const languages = files.map((file) => ({
  name: file.replace('.json', ''),
  keys: new Set(flatten(JSON.parse(readFileSync(join(i18nDir, file), 'utf8')))),
}));

const [reference, ...others] = languages;
let failed = false;

for (const language of others) {
  const missing = [...reference.keys].filter((key) => !language.keys.has(key));
  const extra = [...language.keys].filter((key) => !reference.keys.has(key));

  if (missing.length > 0) {
    failed = true;
    console.error(`i18n parity: ${language.name}.json is missing ${missing.length} key(s) present in ${reference.name}.json:`);
    missing.forEach((key) => console.error(`  - ${key}`));
  }

  if (extra.length > 0) {
    failed = true;
    console.error(`i18n parity: ${language.name}.json has ${extra.length} key(s) absent from ${reference.name}.json:`);
    extra.forEach((key) => console.error(`  - ${key}`));
  }
}

if (failed) {
  process.exit(1);
}

console.log(`i18n parity: ${languages.map((l) => l.name).join(', ')} share ${reference.keys.size} keys.`);
