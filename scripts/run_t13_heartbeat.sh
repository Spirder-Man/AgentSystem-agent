#!/bin/bash
set -e
PROJECT_DIR="/root/autodl-tmp/agent-system"
OUT_DIR="/root/autodl-tmp/test-results/t13_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$OUT_DIR"
LOG="$OUT_DIR/T13_heartbeat.log"

echo "T13 heartbeat test starting at $(date)"
echo "Log: $LOG"

cd "$PROJECT_DIR"
printf "5\n5\n0\n0\n" > /tmp/t13_input.txt

# Start in background
DOTNET_ENVIRONMENT=Production JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx DB_PASSWORD=7758521 \
  dotnet run --project Agent1 -c Release --no-build < /tmp/t13_input.txt > "$LOG" 2>&1 &
PID=$!
echo "PID=$PID"

check_interval=20
max_wait=1800
elapsed=0
last_count=0

while [ $elapsed -lt $max_wait ]; do
  sleep $check_interval
  elapsed=$((elapsed + check_interval))

  if ! kill -0 $PID 2>/dev/null; then
    echo "Process ended naturally at ${elapsed}s"
    break
  fi

  if grep -q "综合评级" "$LOG" 2>/dev/null; then
    echo "Completion marker found at ${elapsed}s, waiting 5s..."
    sleep 5
    break
  fi

  cases=$(grep -c "✅ 工具触发" "$LOG" 2>/dev/null || echo 0)
  if [ "$cases" != "$last_count" ]; then
    echo "[${elapsed}s] Cases: $cases/63"
    last_count=$cases
  fi
done

if kill -0 $PID 2>/dev/null; then
  echo "Killing process at ${elapsed}s"
  kill $PID 2>/dev/null
  wait $PID 2>/dev/null
fi

echo "Done. Elapsed: ${elapsed}s"
echo "Final cases: $(grep -c '✅ 工具触发' "$LOG" 2>/dev/null || echo 0)/63"
grep -q "综合评级" "$LOG" && echo "RESULT: COMPLETED" || echo "RESULT: INCOMPLETE"
