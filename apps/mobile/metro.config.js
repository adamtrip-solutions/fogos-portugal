// Learn more: https://docs.expo.dev/guides/monorepos/
const { getDefaultConfig } = require('expo/metro-config')
const path = require('node:path')

const projectRoot = __dirname
const monorepoRoot = path.resolve(projectRoot, '../..')

const config = getDefaultConfig(projectRoot)

// 1. Watch the whole monorepo so Metro picks up the workspace packages
//    (@fogos/api-client, @fogos/ui-tokens) that are consumed straight from
//    their TypeScript source — no build step.
config.watchFolders = [monorepoRoot]

// 2. Resolve modules from both the app and the repo-root node_modules (pnpm
//    hoists shared deps to the root store).
config.resolver.nodeModulesPaths = [
  path.resolve(projectRoot, 'node_modules'),
  path.resolve(monorepoRoot, 'node_modules'),
]

module.exports = config
