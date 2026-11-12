#!/usr/bin/env bash
# Redeploy Tessyn Web to the `safe` server. Run from a machine with key-auth ssh/scp access (Git Bash is fine).
# Prerequisite: deploy/publish.ps1 has produced artifacts/tessyn-web.tar.gz.
# Usage: bash redeploy.sh [path-to-tarball]
#
# Server layout (see 99-sec-fix-report): service user `tessyn-web`; /var/www/tessyn owned masteruser:tessyn-web (750, files 640,
# +x kept) and read-only for the service; /var/lib/tessyn-web (+ keys) tessyn-web 700; /etc/tessyn-web root:tessyn-web 750 with
# the config file root:tessyn-web 640. The first run on a box also creates the user and fixes those permissions (idempotent).
set -euo pipefail

HOST="${TESSYN_DEPLOY_HOST:-safe}"
APP_DIR=/var/www/tessyn
STATE_DIR=/var/lib/tessyn-web
CONF_DIR=/etc/tessyn-web
UNIT=tessyn-web
SVC_USER=tessyn-web
DEPLOY_USER=masteruser
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARBALL="${1:-$HERE/../artifacts/tessyn-web.tar.gz}"
[ -f "$TARBALL" ] || { echo "Tarball not found: $TARBALL" >&2; exit 1; }

echo "== upload"
scp "$TARBALL" "$HOST:/tmp/tessyn-web.tar.gz"
scp "$HERE/tessyn-web.service" "$HOST:/tmp/tessyn-web.service"

echo "== service user, unit, permissions, replace, start"
ssh "$HOST" "set -e
  id $SVC_USER >/dev/null 2>&1 || sudo useradd --system --no-create-home --shell /usr/sbin/nologin $SVC_USER
  sudo install -m 644 /tmp/tessyn-web.service /etc/systemd/system/$UNIT.service
  sudo rm -f /tmp/tessyn-web.service
  sudo systemctl daemon-reload
  sudo systemctl enable $UNIT >/dev/null 2>&1
  if systemctl is-active --quiet $UNIT; then sudo systemctl stop $UNIT; fi

  sudo mkdir -p $STATE_DIR/keys
  sudo chown -R $SVC_USER:$SVC_USER $STATE_DIR
  sudo chmod 700 $STATE_DIR $STATE_DIR/keys
  if sudo test -d $CONF_DIR; then
    sudo chown root:$SVC_USER $CONF_DIR
    sudo chmod 750 $CONF_DIR
    if sudo test -f $CONF_DIR/appsettings.Production.json; then
      sudo chown root:$SVC_USER $CONF_DIR/appsettings.Production.json
      sudo chmod 640 $CONF_DIR/appsettings.Production.json
    fi
  fi

  sudo find $APP_DIR -mindepth 1 -delete
  sudo tar -xzf /tmp/tessyn-web.tar.gz -C $APP_DIR
  sudo chmod +x $APP_DIR/Tessyn.Web
  sudo chown -R $DEPLOY_USER:$SVC_USER $APP_DIR
  sudo chmod -R u=rwX,g=rX,o= $APP_DIR   # tar applies the archive root mode; normalise to 750 / files 640 (+x kept)
  sudo rm -f /tmp/tessyn-web.tar.gz
  sudo systemctl start $UNIT"

echo "== health check (on the box: the public /healthz no longer reveals the link state)"
for i in 1 2 3 4 5 6 7 8 9 10; do
  if out=$(ssh "$HOST" "curl -fsS --max-time 5 http://127.0.0.1:5320/healthz" 2>/dev/null); then
    echo "healthz (loopback): $out"
    echo "healthz (public):   $(curl -fsS --max-time 5 https://tessyn.vvinogradoff.com/healthz)"
    exit 0
  fi
  sleep 2
done
echo "Health check FAILED; see: ssh $HOST 'sudo journalctl -u $UNIT -n 50'" >&2
exit 1
