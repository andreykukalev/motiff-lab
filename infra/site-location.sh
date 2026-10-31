#!/usr/bin/env bash
# Prints the region of the existing Static Web App ($SWA_NAME in $RESOURCE_GROUP) as main.bicep names it
# ("West Europe" -> westeurope), or main.bicepparam's region when there is no site yet. A site can't change
# region, so CI provisions main.bicep with the region the site already has.
set -euo pipefail
location="$(az staticwebapp show --name "$SWA_NAME" --resource-group "$RESOURCE_GROUP" --query location --output tsv 2>/dev/null || true)"
if [[ -z "$location" ]]; then
  location="$(sed -n "s/^param location = '\(.*\)'$/\1/p" "$(dirname "$0")/main.bicepparam")"
fi
echo "$location" | tr -d ' ' | tr '[:upper:]' '[:lower:]'
