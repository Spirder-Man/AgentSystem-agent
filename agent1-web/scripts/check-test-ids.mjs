// ============================================================
// check-test-ids.mjs — test-id 契约校验脚本
//
// 功能:
//   1. 提取 src/test-ids.ts 中 ALL_TEST_IDS 注册的所有合法 test-id
//   2. 扫描 e2e-real/**/*.ts 中所有 data-testid 引用
//   3. 报告未注册的 test-id（硬编码或拼写错误）
//
// 集成: npm run pretest:e2e:real 时在 health-check 后运行
// 用法: node scripts/check-test-ids.mjs
// ============================================================

import { readFileSync } from 'node:fs';
import { readdir, readFile } from 'node:fs/promises';
import { join, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = fileURLToPath(new URL('.', import.meta.url));
const root = join(__dirname, '..');

// ── Step 1: 从 test-ids.ts 提取所有已注册的 test-id ──
async function extractRegisteredIds() {
  const src = readFileSync(join(root, 'src', 'test-ids.ts'), 'utf-8');
  const ids = new Set();

  // 匹配 COMPLIANCE_CHECK.xxx 或直接字符串值的形式
  // 提取 ALL_TEST_IDS 数组中的所有字符串
  const allIdsMatch = src.match(/export const ALL_TEST_IDS[\s\S]*?\]/);
  if (allIdsMatch) {
    const strVals = allIdsMatch[0].match(/'([^']+)'/g);
    if (strVals) {
      for (const s of strVals) {
        ids.add(s.replace(/'/g, ''));
      }
    }
  }

  // 也提取直接导出的常量值
  const constPattern = /:\s*'([^']+)'/g;
  let m;
  while ((m = constPattern.exec(src)) !== null) {
    ids.add(m[1]);
  }

  return ids;
}

// ── Step 2: 递归收集 .ts 文件 ──
async function collectTsFiles(dir) {
  const files = [];
  const entries = await readdir(dir, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = join(dir, entry.name);
    if (entry.isDirectory() && entry.name !== 'node_modules') {
      files.push(...await collectTsFiles(fullPath));
    } else if (entry.isFile() && extname(entry.name) === '.ts') {
      files.push(fullPath);
    }
  }
  return files;
}

// ── Step 3: 扫描每个文件中的 data-testid 引用 ──
async function scanForTestIds(filePath) {
  const content = await readFile(filePath, 'utf-8');
  const refs = [];

  // 匹配 data-testid="xxx" 或 data-testid={`xxx`} 或 [data-testid="xxx"]
  const pattern = /data-testid\s*=\s*["'`]([^"'`]+)["'`]/g;
  let m;
  while ((m = pattern.exec(content)) !== null) {
    const val = m[1];
    // 跳过模板变量（如 ${COMPLIANCE_CHECK.xxx}）
    if (val.includes('${')) continue;
    refs.push(val);
  }
  return refs;
}

// ── Main ──
async function main() {
  console.log('🔍 check-test-ids: 扫描 test-id 契约合规性...\n');

  // 1. 提取注册表
  const registered = await extractRegisteredIds();
  console.log(`  ✓ 已注册 test-id: ${registered.size} 个`);

  // 2. 扫描 e2e-real 目录
  const e2eDir = join(root, 'e2e-real');
  const tsFiles = await collectTsFiles(e2eDir);
  console.log(`  ✓ 扫描文件: ${tsFiles.length} 个\n`);

  let violations = 0;
  for (const file of tsFiles) {
    const refs = await scanForTestIds(file);
    for (const ref of refs) {
      if (!registered.has(ref)) {
        violations++;
        const relPath = file.replace(root, '').replace(/^[\\/]/, '');
        console.error(`  ❌ 未注册的 test-id: "${ref}"\n     文件: ${relPath}`);
      }
    }
  }

  if (violations > 0) {
    console.error(`\n🚫 发现 ${violations} 个未注册的 test-id 引用。`);
    console.error('   请在 src/test-ids.ts 的 ALL_TEST_IDS 中注册新 test-id。');
    process.exit(1);
  }

  console.log('  ✅ 所有 test-id 引用均已注册。契约合规。\n');
  process.exit(0);
}

main().catch((err) => {
  console.error('check-test-ids 执行失败:', err.message);
  process.exit(1);
});
