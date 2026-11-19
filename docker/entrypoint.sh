#!/bin/bash
# Sobe o firewall como root e depois troca para o usuário "node" sem nenhum
# privilégio. O Claude nunca roda como root e não consegue desfazer o firewall.
set -euo pipefail

/usr/local/bin/init-firewall.sh

export HOME=/home/node USER=node
exec setpriv --reuid=node --regid=node --init-groups --no-new-privs -- /usr/local/bin/sync-run.sh "$@"
