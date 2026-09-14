#!/usr/bin/env bash
#
# Server bootstrap for VE Ops, per docs/deployment.md. Run as root on the Ubuntu box (over
# Tailscale SSH) with the rest of ops/ copied alongside it:
#
#   scp -r ops deploy@<box>:/tmp/veops-ops && ssh deploy@<box> sudo bash /tmp/veops-ops/setup-server.sh
#
# Safe to re-run — every step is idempotent (existing user/dirs/keys are left alone; unit files,
# sudoers and the deploy scripts are overwritten with the current content). It does two jobs:
#
#   FRESH BOX   creates the service account, the releases/ + current layout, data/log/key-ring
#               directories, backup helpers, sudoers, systemd units, deploy scripts and THIS app's
#               forced-command deploy key. Enables the units without starting them.
#   UPGRADE     a box still on the in-place /opt/vesessionmanager/{web,worker} layout is moved to
#               releases/<tag>/ + current (step 3b, guarded — only runs when it detects that layout),
#               and its logs are moved to /var/lib/vesessionmanager/logs. Both units are stopped for
#               the move and started again on the new paths, a few seconds.
#
# Does NOT:
#   - touch Tailscale (the same tag:ci OAuth client as NcsScheduler is already usable)
#   - add GitHub Actions secrets (manual, listed at the end — the deploy key it prints goes there)
#   - configure Apache or certbot
#
# Every server name in here is deliberately still `vesessionmanager` (the repo is VeOps): these are
# live paths, accounts and unit names, and renaming them is a migration nobody has asked for.

set -euo pipefail

APP_SLUG="vesessionmanager"
SERVICE_ACCOUNT="vesessionmanager"
DEPLOY_USER="deploy"
DEPLOY_PATH="/opt/${APP_SLUG}"
DATA_PATH="/var/lib/${APP_SLUG}"
LOG_PATH="${DATA_PATH}/logs"
# Deliberately a SIBLING of DATA_PATH, not a subdirectory of it. The Data Protection key ring
# decrypts the credential columns inside the database, so one archive of one directory must not
# be able to carry both halves. Moved out of ${DATA_PATH} on 2026-08-10 -- see
# docs/deployment.md, "The key ring moved out of the database directory".
KEYRING_PATH="/var/lib/${APP_SLUG}-keys"
WORKER_SERVICE="${APP_SLUG}-worker"
WEB_SERVICE="${APP_SLUG}-web"
WEB_PORT="5100"
HERE="$(cd "$(dirname "$0")" && pwd)"

if [[ $EUID -ne 0 ]]; then
  echo "Must run as root (sudo bash setup-server.sh)." >&2
  exit 1
fi

echo "== 1/7: Runtime service account (${SERVICE_ACCOUNT}) =="
if id -u "$SERVICE_ACCOUNT" &>/dev/null; then
  echo "  ${SERVICE_ACCOUNT} already exists, skipping."
else
  useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_ACCOUNT"
  echo "  Created system account ${SERVICE_ACCOUNT}."
fi

echo "== 2/7: Deploy account (${DEPLOY_USER}) =="
if id -u "$DEPLOY_USER" &>/dev/null; then
  echo "  ${DEPLOY_USER} already exists (expected — shared with NcsScheduler), skipping creation."
else
  useradd -m -s /bin/bash "$DEPLOY_USER"
  echo "  Created ${DEPLOY_USER}."
fi
# The deploy user reads and writes the release tree through the service account's group (#337).
usermod -a -G "$SERVICE_ACCOUNT" "$DEPLOY_USER"
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 0700 "/home/${DEPLOY_USER}/.ssh"

echo "== 3/7: App + data directories =="
mkdir -p "${DEPLOY_PATH}/releases" "${DEPLOY_PATH}/ops" "${DATA_PATH}" "${LOG_PATH}"
chown -R "${SERVICE_ACCOUNT}:${SERVICE_ACCOUNT}" "${DATA_PATH}"
# Serilog writes here (absolute path in both appsettings.Production.json). A relative logs/ would
# resolve under WorkingDirectory, i.e. inside one release directory, and be pruned with it.
chmod 2750 "${LOG_PATH}"

# The release tree is owned by the DEPLOY user with the service account's group (#254). That is what
# lets the workflow rsync into it as itself -- no `--rsync-path="sudo rsync"`, and therefore no
# `/usr/bin/rsync *` sudoers rule, which was root-equivalent and made every other rule decorative.
# The service account reads through the group and never writes here; the database, key ring and
# logs all live under /var/lib. setgid so anything created later keeps that group.
chown -R "${DEPLOY_USER}:${SERVICE_ACCOUNT}" "${DEPLOY_PATH}"
find "${DEPLOY_PATH}" -type d -exec chmod g+s {} +
# Except ops/: root-owned, so the deploy user can run the scripts in it but not replace them.
chown "root:${SERVICE_ACCOUNT}" "${DEPLOY_PATH}/ops"; chmod 0750 "${DEPLOY_PATH}/ops"
echo "  ${DEPLOY_PATH}/releases owned by ${DEPLOY_USER}:${SERVICE_ACCOUNT} (setgid); ${DATA_PATH} and ${LOG_PATH} by ${SERVICE_ACCOUNT}."

# Key ring: its own directory, and 0700 rather than the 0755 the others get -- nothing but the
# service account has any reason to read it.
mkdir -p "${KEYRING_PATH}"
chown "${SERVICE_ACCOUNT}:${SERVICE_ACCOUNT}" "${KEYRING_PATH}"
chmod 700 "${KEYRING_PATH}"
echo "  ${KEYRING_PATH} present (0700, ${SERVICE_ACCOUNT})."
if [[ -d "${DATA_PATH}/dataprotection-keys" ]] && ! compgen -G "${KEYRING_PATH}/*.xml" > /dev/null; then
  echo
  echo "  ####################################################################"
  echo "  # WARNING: keys still live at ${DATA_PATH}/dataprotection-keys"
  echo "  # and ${KEYRING_PATH} is empty."
  echo "  #"
  echo "  # This is an UPGRADE of an existing box, not a fresh install. Copy"
  echo "  # the keys across BEFORE deploying, or every stored credential"
  echo "  # becomes unreadable (silently -- the app falls back to treating"
  echo "  # ciphertext as plaintext):"
  echo "  #"
  echo "  #   sudo cp ${DATA_PATH}/dataprotection-keys/*.xml ${KEYRING_PATH}/"
  echo "  #   sudo chown ${SERVICE_ACCOUNT}:${SERVICE_ACCOUNT} ${KEYRING_PATH}/*.xml"
  echo "  #"
  echo "  # Then deploy, confirm the startup log says 'key ring verified',"
  echo "  # and only then remove the old directory."
  echo "  ####################################################################"
  echo
fi

echo "== 3b: In-place layout -> releases/ + current (only if the old layout is present) =="
# A box deployed before 2026-09 has the build directly at ${DEPLOY_PATH}/{web,worker} and the units
# pointing there. Move that build under releases/<tag> and link current to it, so the next tag
# deploy lands beside it rather than over it. Tag name: the footer's version, read from the DLL's
# InformationalVersion, falling back to "pre-releases" if it cannot be read. The exact name only
# has to be unique -- it is what `current` points at until the next deploy replaces it.
MIGRATED_LAYOUT=0
if [[ -d "${DEPLOY_PATH}/web" && ! -L "${DEPLOY_PATH}/current" ]]; then
  # Pass the tag as $1 to skip the guess: sudo bash setup-server.sh 2026.09.0
  old_tag="${1:-}"
  [[ -n "$old_tag" ]] || old_tag="$(strings "${DEPLOY_PATH}/web/VeOps.Web.dll" 2>/dev/null | grep -oE '2[0-9]{3}\.[0-9]{2}\.[0-9]+' | head -1 || true)"
  old_tag="${old_tag:-pre-releases}"
  target="${DEPLOY_PATH}/releases/${old_tag}"
  echo "  Old layout found. Moving ${DEPLOY_PATH}/{web,worker} -> ${target}/ and stopping both units for it."
  systemctl stop "$WEB_SERVICE" || true
  systemctl stop "$WORKER_SERVICE" || true
  mkdir -p "$target"
  mv "${DEPLOY_PATH}/web" "${target}/web"
  mv "${DEPLOY_PATH}/worker" "${target}/worker"
  # Log history goes with the service account, to where Serilog now writes. Prefixes differ
  # (web-*.log / worker-*.log), so one directory holds both without collision. The old release's
  # own logs/ directory STAYS, owned by the service account: that build still carries the relative
  # "logs/" path and keeps writing there until the next tag replaces it (prune removes it later).
  for h in web worker; do
    if [[ -d "${target}/${h}/logs" ]]; then
      find "${target}/${h}/logs" -maxdepth 1 -type f -name '*.log' -exec mv -n -t "${LOG_PATH}" {} +
    fi
  done
  chown -R "${SERVICE_ACCOUNT}:${SERVICE_ACCOUNT}" "${LOG_PATH}"
  ln -sfn "$target" "${DEPLOY_PATH}/current"
  chown -R "${DEPLOY_USER}:${SERVICE_ACCOUNT}" "${DEPLOY_PATH}/releases"
  chown -h "${DEPLOY_USER}:${SERVICE_ACCOUNT}" "${DEPLOY_PATH}/current"
  find "${DEPLOY_PATH}/releases" -type d -exec chmod g+s {} +
  for h in web worker; do
    [[ -d "${target}/${h}/logs" ]] && chown -R "${SERVICE_ACCOUNT}:${SERVICE_ACCOUNT}" "${target}/${h}/logs"
  done
  MIGRATED_LAYOUT=1
  echo "  current -> ${target}. Units are restarted on the new paths at the end of this script."
elif [[ -L "${DEPLOY_PATH}/current" ]]; then
  echo "  current -> $(readlink -f "${DEPLOY_PATH}/current"), already on the releases/ layout."
else
  echo "  Fresh box: no build present yet; the first tag deploy creates current."
fi

echo "== 4/7: Backup helpers + sudoers rule for ${DEPLOY_USER} =="
# The privileged parts of a deploy live in two argument-free scripts rather than in `sudo rsync`
# (#254). Copied from the repo's ops/ directory alongside this script.
BACKUP_KEYRING="/usr/local/sbin/${APP_SLUG}-backup-keyring"
BACKUP_DB="/usr/local/sbin/${APP_SLUG}-backup-db"
for pair in "${APP_SLUG}-backup-keyring:${BACKUP_KEYRING}" "${APP_SLUG}-backup-db:${BACKUP_DB}"; do
  src="${pair%%:*}"; dest="${pair##*:}"
  if [[ -f "${HERE}/${src}" ]]; then
    install -o root -g root -m 0755 "${HERE}/${src}" "$dest"
    echo "  Installed ${dest}."
  elif [[ -f "$dest" ]]; then
    chown root:root "$dest"; chmod 0755 "$dest"
    echo "  ${dest} already present."
  else
    echo "  ERROR: ${src} not found beside this script and ${dest} does not exist." >&2
    echo "  Copy ops/${src} to the server and re-run." >&2
    exit 1
  fi
done

SUDOERS_FILE="/etc/sudoers.d/${APP_SLUG}-deploy"
SUDOERS_TMP="$(mktemp)"
# NO WILDCARDS, anywhere (#254). The previous version granted `/usr/bin/rsync *`, which is
# root-equivalent: sudo rsync can read or overwrite any file on the box -- /etc/shadow,
# /root/.ssh/authorized_keys, this very file -- and --rsync-path/-e can execute commands. So whoever
# held the deploy SSH key owned the server, and the narrow systemctl rules beside it meant nothing.
# `/usr/bin/cp <db> *` was the same shape and is gone with it. sudo matches the WHOLE command line,
# so each journalctl entry must appear exactly as ops/deploy-release runs it. `restart` per unit is
# for a person on the box; deploy-release itself only uses stop/start.
# NOTE on the `""` after each helper (learned the hard way, 2026-08-11): in sudoers a command
# listed WITHOUT arguments permits the user to run it with ANY arguments. Only an explicit `""`
# means "no arguments". Without it the entry is far looser than it looks, and the only thing
# stopping an argument reaching the script is the script's own guard.
cat > "$SUDOERS_TMP" <<EOF
Defaults:${DEPLOY_USER} !requiretty
${DEPLOY_USER} ALL=(root) NOPASSWD: /usr/bin/systemctl stop ${WORKER_SERVICE}, /usr/bin/systemctl stop ${WEB_SERVICE}, /usr/bin/systemctl start ${WORKER_SERVICE}, /usr/bin/systemctl start ${WEB_SERVICE}, /usr/bin/systemctl restart ${WORKER_SERVICE}, /usr/bin/systemctl restart ${WEB_SERVICE}, ${BACKUP_KEYRING} "", ${BACKUP_DB} "", /usr/bin/journalctl -u ${WORKER_SERVICE} -n 50 --no-pager, /usr/bin/journalctl -u ${WEB_SERVICE} -n 50 --no-pager
EOF
chmod 0440 "$SUDOERS_TMP"
if visudo -c -f "$SUDOERS_TMP" &>/dev/null; then
  mv "$SUDOERS_TMP" "$SUDOERS_FILE"
  chmod 0440 "$SUDOERS_FILE"
  echo "  Wrote and validated ${SUDOERS_FILE}."
else
  echo "  ERROR: generated sudoers file failed visudo -c validation; left at ${SUDOERS_TMP} for inspection, NOT installed." >&2
  exit 1
fi

echo "== 5/7: systemd unit files =="
# Both point through the `current` symlink. systemd resolves it at each start, so a deploy is
# "flip the link, start the unit" and a rollback is the same thing pointed the other way.
cat > "/etc/systemd/system/${WORKER_SERVICE}.service" <<EOF
[Unit]
Description=VE Ops Worker
After=network.target

[Service]
WorkingDirectory=${DEPLOY_PATH}/current/worker
ExecStart=/usr/bin/dotnet ${DEPLOY_PATH}/current/worker/VeOps.Worker.dll
Restart=always
RestartSec=10
User=${SERVICE_ACCOUNT}
# The Worker is a plain generic Host, not ASP.NET Core -- it reads DOTNET_ENVIRONMENT, not
# ASPNETCORE_ENVIRONMENT. Its own default when unset is Production anyway, but set it explicitly
# so it's never in question (see CLAUDE.md's "Worker Service reads DOTNET_ENVIRONMENT" gotcha).
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
EOF

cat > "/etc/systemd/system/${WEB_SERVICE}.service" <<EOF
[Unit]
Description=VE Ops Web
After=network.target

[Service]
WorkingDirectory=${DEPLOY_PATH}/current/web
ExecStart=/usr/bin/dotnet ${DEPLOY_PATH}/current/web/VeOps.Web.dll
Restart=always
RestartSec=10
User=${SERVICE_ACCOUNT}
Environment=ASPNETCORE_ENVIRONMENT=Production
# Without this, Kestrel falls back to its own default rather than the port Apache/the deploy
# health check expect -- set it explicitly so the real listening port is never in question. Must
# match PORT in ops/deploy-release.
Environment=ASPNETCORE_URLS=http://localhost:${WEB_PORT}

[Install]
WantedBy=multi-user.target
EOF
echo "  Wrote ${WORKER_SERVICE}.service and ${WEB_SERVICE}.service (paths via ${DEPLOY_PATH}/current)."

echo "== 6/7: Deploy scripts + this app's forced-command key =="
for f in deploy-release ssh-deploy-command; do
  if [[ -f "${HERE}/${f}" ]]; then
    # root-owned: deploy runs these (through the group) but cannot edit them, so a build uploaded
    # with a stolen key cannot also rewrite what judges it.
    install -o root -g "${SERVICE_ACCOUNT}" -m 0750 "${HERE}/${f}" "${DEPLOY_PATH}/ops/${f}"
    echo "  Installed ${DEPLOY_PATH}/ops/${f}."
  else
    echo "  ERROR: ${f} not found beside this script." >&2; exit 1
  fi
done
# One keypair per app, restricted by the forced command to this app's releases/ directory and its
# two verbs. The shared deploy key NcsScheduler uses is left alone -- but once THIS repo's
# SSH_PRIVATE_KEY secret holds the new key, nothing of VeOps' depends on the shared one any more.
KEY="/home/${DEPLOY_USER}/.ssh/${APP_SLUG}-ci"
AUTH="/home/${DEPLOY_USER}/.ssh/authorized_keys"
OPTS="command=\"${DEPLOY_PATH}/ops/ssh-deploy-command\",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty"
if [[ ! -f "$KEY" ]]; then
  sudo -u "${DEPLOY_USER}" ssh-keygen -q -t ed25519 -N "" -C "${APP_SLUG}-ci" -f "$KEY"
  echo "  Generated ${KEY}."
fi
touch "$AUTH"; chown "${DEPLOY_USER}:${DEPLOY_USER}" "$AUTH"; chmod 600 "$AUTH"
if ! grep -qF "$(cut -d' ' -f2 "$KEY.pub")" "$AUTH"; then
  echo "$OPTS $(cat "$KEY.pub")" >> "$AUTH"
  echo "  Installed the public half in ${AUTH} with the forced command."
else
  echo "  Public half already in ${AUTH}."
fi

echo "== 7/7: Reload + enable =="
systemctl daemon-reload
systemctl enable "$WORKER_SERVICE" "$WEB_SERVICE"
if [[ $MIGRATED_LAYOUT -eq 1 ]]; then
  systemctl start "$WORKER_SERVICE"
  sleep 5
  systemctl is-active --quiet "$WORKER_SERVICE" || { echo "  ERROR: worker did not come back on the new path:"; journalctl -u "$WORKER_SERVICE" -n 30 --no-pager; exit 1; }
  systemctl start "$WEB_SERVICE"
  echo "  Both units restarted on ${DEPLOY_PATH}/current. Check the site answers before pushing a tag."
elif [[ -L "${DEPLOY_PATH}/current" ]]; then
  echo "  Units already on the releases/ layout; not restarting anything."
else
  echo "  Enabled both services for boot-persistence. Not starting them: there's no release yet on a"
  echo "  fresh box, so ExecStart would just fail. The first tag deploy starts them for real."
fi

cat <<SUMMARY

==============================================================================
Server bootstrap complete. Remaining steps (not done by this script):

  1. Add / update these GitHub Actions secrets on the VeOps repo
     (Settings > Secrets and variables > Actions):
       TS_OAUTH_CLIENT_ID   same value as NcsScheduler's repo secret
       TS_OAUTH_SECRET      same value as NcsScheduler's repo secret
       SSH_PRIVATE_KEY      THIS app's key -- the contents of ${KEY}
                            (then remove it from the box: shred -u ${KEY})
       DEPLOY_HOST          this server's Tailscale hostname
       DEPLOY_HOST_KEY      output of:  ssh-keyscan -t ed25519 localhost 2>/dev/null | cut -d" " -f2-
       DEPLOY_USER          ${DEPLOY_USER}
  2. Push a tag to trigger the first deploy on the new layout:
       git tag -a 2026.09.1 -m "..." && git push --tags
     Watch it in the repo's Actions tab; docs/runbooks/deploy-a-release.md says what healthy
     looks like.
  3. Fresh box only: Apache vhost + certbot from docs/deployment.md, and the off-box backup job
     (BACKUP.md) -- the key ring backed up separately from the database, always.
==============================================================================
SUMMARY
