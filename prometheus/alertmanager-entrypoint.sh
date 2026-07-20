#!/bin/sh
# ═══════════════════════════════════════════════
# Alertmanager entrypoint — 通过 envsubst 注入 .env 凭证
# 将 alertmanager.tmpl → alertmanager.yml 后启动
# ═══════════════════════════════════════════════
set -e

envsubst '$ALERT_EMAIL_USER $ALERT_EMAIL_PASSWORD $ALERT_RECIPIENT_EMAILS $ALERT_SMTP_HOST' \
  < /etc/alertmanager/alertmanager.tmpl > /etc/alertmanager/alertmanager.yml

exec /bin/alertmanager --config.file=/etc/alertmanager/alertmanager.yml --storage.path=/alertmanager
