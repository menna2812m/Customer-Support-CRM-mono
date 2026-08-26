// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

/**
 * Constitutional rules that are cheap to break and expensive to find in review are enforced here
 * as lint failures (Constitution I, VI, VII).
 */
module.exports = tseslint.config(
  {
    ignores: ['dist/**', 'node_modules/**', '.angular/**', 'coverage/**', '**/*.min.js'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'crm', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'crm', style: 'kebab-case' },
      ],
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],
      '@typescript-eslint/explicit-member-accessibility': 'off',
      '@typescript-eslint/consistent-type-definitions': ['error', 'interface'],

      // No deep library imports: only the published entry point is part of the contract
      // (spec FR-003, contracts/frontend-contracts.md).
      '@typescript-eslint/no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: [
                '@crm/*/src/**',
                '@crm/*/lib/**',
                '**/projects/core/src/**',
                '**/projects/ui/src/**',
              ],
              message:
                'Import from the library entry point (@crm/core, @crm/ui). Deep imports bypass the public API surface.',
            },
          ],
        },
      ],
    },
  },
  {
    // Constitution VI: HTTP access is encapsulated in feature data-access services.
    files: ['**/*.ts'],
    ignores: ['**/*-api.service.ts', '**/*.spec.ts', 'projects/core/src/lib/**'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: '@angular/common/http',
              importNames: ['HttpClient'],
              message:
                'Components must not call HttpClient. Put the call in a *-api.service.ts data-access service.',
            },
          ],
        },
      ],
    },
  },
  {
    // Constitution I: a feature must not depend on another feature's internals.
    //
    // Caught here: any import that spells out a feature path, which is what editors generate.
    // Not caught: a hand-written sibling shorthand from a feature-root file; review covers that.
    files: ['projects/*/src/app/features/**/*.ts'],
    rules: {
      '@typescript-eslint/no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/features/*/**', '../../features/**', '../../../features/**'],
              message:
                'Features are independent. Move anything two features need into @crm/core or @crm/ui.',
            },
          ],
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: {},
  },
);
