#!/bin/bash
echo "=== 仓库中最大的 JS/JSON 文件 ==="
git ls-files | grep -E '\.js$|\.json$' | while read f; do
  SIZE=$(wc -c < "$f" 2>/dev/null | tr -d ' ')
  if [ "$SIZE" -gt 10000 ]; then
    echo "$SIZE $f"
  fi
done | sort -rn | head -20

echo ""
echo "=== .vite/deps 目录 (编译产物，不应提交) ==="
ls -la agent1-web/.vite/deps/ 2>/dev/null | head -20

echo ""
echo "=== node_modules 是否被跟踪 ==="
git ls-files node_modules/ 2>/dev/null | head -5

echo ""
echo "=== 所有被跟踪的 .js 文件数量 ==="
git ls-files | grep '\.js$' | wc -l
echo "=== 所有被跟踪的 .json 文件数量 ==="
git ls-files | grep '\.json$' | wc -l
