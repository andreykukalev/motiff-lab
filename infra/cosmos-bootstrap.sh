#!/usr/bin/env bash
# One-time setup of Motiff Lab's database in the shared, free-tier Cosmos DB account in rg-shared
# (decision record 0014). Run from the repository root, signed in with `az login` to the subscription
# that holds rg-shared. Safe to re-run.
#
# It only adds: the database `motifflab` with its container, an identity for the Motiff Lab API, and that
# identity's data access to this one database. It changes nothing on the account itself or in any other
# database, and it stops before creating anything if the database would take the account past the free
# tier's 1000 RU/s.
set -euo pipefail

SHARED_GROUP="${SHARED_GROUP:-rg-shared}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-motifflab}"
DATABASE="${DATABASE:-motifflab}"
CONTAINER="${CONTAINER:-projects}"
THROUGHPUT="${THROUGHPUT:-400}" # the least a database with shared throughput can have
FREE_TIER_RU=1000
IDENTITY="${IDENTITY:-id-motifflab-api}"
IDENTITY_LOCATION="${IDENTITY_LOCATION:-polandcentral}"
DATA_CONTRIBUTOR=00000000-0000-0000-0000-000000000002 # Cosmos DB Built-in Data Contributor

echo "Subscription: $(az account show --query name --output tsv)"

account="${COSMOS_ACCOUNT:-}"
if [[ -z "$account" ]]; then
  mapfile -t accounts < <(az cosmosdb list --resource-group "$SHARED_GROUP" \
    --query "[?enableFreeTier].name" --output tsv | tr -d '\r')
  if [[ ${#accounts[@]} -ne 1 ]]; then
    echo "Expected one free-tier Cosmos DB account in $SHARED_GROUP, found ${#accounts[@]}: ${accounts[*]:-none}." >&2
    echo "Name it with COSMOS_ACCOUNT=<name> $0" >&2
    exit 1
  fi
  account="${accounts[0]}"
fi

kind="$(az cosmosdb show --name "$account" --resource-group "$SHARED_GROUP" --query kind --output tsv | tr -d '\r')"
if [[ "$kind" != GlobalDocumentDB ]]; then
  echo "$account is a $kind account; Motiff Lab needs the NoSQL API (GlobalDocumentDB)." >&2
  exit 1
fi
serverless="$(az cosmosdb show --name "$account" --resource-group "$SHARED_GROUP" \
  --query "length(capabilities[?name=='EnableServerless'])" --output tsv | tr -d '\r')"

# Throughput of a database or container, or nothing when it has none of its own.
# Autoscale counts at its maximum, which is what the free tier is measured against at the busiest hour.
throughput() {
  az cosmosdb sql "$@" --account-name "$account" --resource-group "$SHARED_GROUP" \
    --query "resource.autoscaleSettings.maxThroughput || resource.throughput" --output tsv 2>/dev/null | tr -d '\r' || true
}

exists="$(az cosmosdb sql database list --account-name "$account" --resource-group "$SHARED_GROUP" \
  --query "length([?name=='$DATABASE'])" --output tsv | tr -d '\r')"

if [[ "$exists" == 0 && "$serverless" == 0 ]]; then
  used=0
  while IFS= read -r db; do
    [[ -z "$db" ]] && continue
    shared="$(throughput database throughput show --name "$db")"
    if [[ -n "$shared" ]]; then
      echo "  database $db: $shared RU/s"
      used=$((used + shared))
    fi
    while IFS= read -r coll; do
      [[ -z "$coll" ]] && continue
      own="$(throughput container throughput show --database-name "$db" --name "$coll")"
      if [[ -n "$own" ]]; then
        echo "  container $db/$coll: $own RU/s"
        used=$((used + own))
      fi
    done < <(az cosmosdb sql container list --account-name "$account" --resource-group "$SHARED_GROUP" \
      --database-name "$db" --query "[].name" --output tsv | tr -d '\r')
  done < <(az cosmosdb sql database list --account-name "$account" --resource-group "$SHARED_GROUP" \
    --query "[].name" --output tsv | tr -d '\r')

  echo "$account already uses $used of the free tier's $FREE_TIER_RU RU/s."
  if (( used + THROUGHPUT > FREE_TIER_RU )); then
    echo "Adding $THROUGHPUT RU/s for $DATABASE would go past the free tier and cost money. Nothing was created." >&2
    exit 1
  fi
fi

echo "Account: $account ($([[ "$serverless" == 0 ]] && echo "free tier, $DATABASE gets $THROUGHPUT RU/s shared" || echo serverless))"
echo "Creates: database $DATABASE, container $CONTAINER (partition /ownerId), identity $IDENTITY in $RESOURCE_GROUP"
read -r -p "Continue? [y/N] " answer
[[ "$answer" == [yY] ]] || exit 1

if [[ "$exists" == 0 ]]; then
  if [[ "$serverless" == 0 ]]; then
    az cosmosdb sql database create --account-name "$account" --resource-group "$SHARED_GROUP" \
      --name "$DATABASE" --throughput "$THROUGHPUT" --output none
  else
    az cosmosdb sql database create --account-name "$account" --resource-group "$SHARED_GROUP" \
      --name "$DATABASE" --output none
  fi
fi

# One container for everything a player keeps, partitioned by player, so a player's data is one partition.
az cosmosdb sql container create --account-name "$account" --resource-group "$SHARED_GROUP" \
  --database-name "$DATABASE" --name "$CONTAINER" --partition-key-path /ownerId --output none 2>/dev/null \
  || echo "Container $CONTAINER already exists."

az identity create --name "$IDENTITY" --resource-group "$RESOURCE_GROUP" --location "$IDENTITY_LOCATION" \
  --tags app=motiff-lab service=api --output none
principal="$(az identity show --name "$IDENTITY" --resource-group "$RESOURCE_GROUP" --query principalId --output tsv | tr -d '\r')"

# Data access to this database only, never to the account's other databases or its keys.
assigned="$(az cosmosdb sql role assignment list --account-name "$account" --resource-group "$SHARED_GROUP" \
  --query "length([?principalId=='$principal'])" --output tsv | tr -d '\r')"
if [[ "$assigned" == 0 ]]; then
  az cosmosdb sql role assignment create --account-name "$account" --resource-group "$SHARED_GROUP" \
    --role-definition-id "$DATA_CONTRIBUTOR" --principal-id "$principal" --scope "/dbs/$DATABASE" --output none
fi

echo "Done. Endpoint: $(az cosmosdb show --name "$account" --resource-group "$SHARED_GROUP" --query documentEndpoint --output tsv)"
