#!/usr/bin/env bash
# One-time setup for Azure hosting. Run from the repository root, signed in with
# `az login` (subscription selected) and `gh auth login`. Safe to re-run.
set -euo pipefail

REPO="${REPO:-andreykukalev/motiff-lab}"
# Repository as GitHub writes it in the OIDC subject claim: owner@ownerId/name@repoId.
OIDC_REPO="${OIDC_REPO:-andreykukalev@130970440/motiff-lab@1430211759}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-motifflab}"
LOCATION="${LOCATION:-westeurope}"

subscription_id="$(az account show --query id --output tsv)"
tenant_id="$(az account show --query tenantId --output tsv)"
echo "Subscription: $(az account show --query name --output tsv) ($subscription_id)"
echo "Resource group: $RESOURCE_GROUP ($LOCATION), repository: $REPO"
read -r -p "Continue? [y/N] " answer
[[ "$answer" == [yY] ]] || exit 1

az group create --name "$RESOURCE_GROUP" --location "$LOCATION" \
  --tags app=motiff-lab --output none

client_id="$(az deployment group create --resource-group "$RESOURCE_GROUP" \
  --name bootstrap --template-file infra/bootstrap.bicep \
  --parameters repository="$OIDC_REPO" \
  --query properties.outputs.clientId.value --output tsv)"

# First provision, so pull requests have a Static Web App to deploy previews to.
az deployment group create --resource-group "$RESOURCE_GROUP" \
  --name main --template-file infra/main.bicep --parameters infra/main.bicepparam \
  --output none

gh variable set AZURE_CLIENT_ID --repo "$REPO" --body "$client_id"
gh variable set AZURE_TENANT_ID --repo "$REPO" --body "$tenant_id"
gh variable set AZURE_SUBSCRIPTION_ID --repo "$REPO" --body "$subscription_id"
gh variable set AZURE_RESOURCE_GROUP --repo "$REPO" --body "$RESOURCE_GROUP"

echo "Done. Production URL: https://$(az staticwebapp show --name swa-motifflab \
  --resource-group "$RESOURCE_GROUP" --query defaultHostname --output tsv)"
