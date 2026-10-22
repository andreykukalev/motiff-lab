# Infrastructure

Stage 1 needs one Azure resource: a Static Web App on the Free plan in West Europe ([`main.bicep`](main.bicep)). The app runs fully in the browser, so there are no Functions, storage or AI resources yet (see [docs/decisions/0001-stack.md](../docs/decisions/0001-stack.md)).

Static Web Apps is only offered in a few regions, and West Europe is the only EU one, so Sweden Central is not an option for this resource. Static content is served from the global edge; the region holds the app's metadata and, later, its managed Functions.

## Deploys

The `CI` workflow ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) builds and tests, publishes the web app, then deploys the published `wwwroot` with the deployment token:

- **Push to `main`** deploys to production.
- **Pull request** (from this repository) deploys to a preview environment and comments its URL on the PR. Closing the PR deletes the preview.
- Without the `AZURE_STATIC_WEB_APPS_API_TOKEN` secret (and on forks) the deploy step is skipped, not failed.

The Free plan allows 3 preview environments at a time. A fourth open PR fails its deploy until an older PR is closed.

## One-time setup

Requires the Azure CLI (2.53 or later for `.bicepparam`) and, for step 4, the GitHub CLI.

1. Sign in and pick the subscription:

   ```bash
   az login
   az account set --subscription "<subscription name or id>"
   ```

2. Create the resource group:

   ```bash
   az group create --name rg-motifflab --location westeurope
   ```

3. Deploy the Bicep:

   ```bash
   az deployment group create \
     --resource-group rg-motifflab \
     --template-file infra/main.bicep \
     --parameters infra/main.bicepparam
   ```

   The output `defaultHostName` is the production URL.

4. Copy the deployment token into the repository secret:

   ```bash
   az staticwebapp secrets list --name swa-motifflab --resource-group rg-motifflab \
     --query properties.apiKey --output tsv \
     | gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN --repo andreykukalev/motiff-lab
   ```

   Without the GitHub CLI: run the first command alone, then add the value under **Settings → Secrets and variables → Actions → New repository secret** with the name `AZURE_STATIC_WEB_APPS_API_TOKEN`.

5. Re-run the latest `CI` run on `main` (or push a commit) to make the first production deploy.

## Changes later

- Re-running step 3 is safe; the deployment is incremental and idempotent.
- To rotate the token: `az staticwebapp secrets reset-api-key --name swa-motifflab --resource-group rg-motifflab`, then repeat step 4.
- To add a custom domain, use `Microsoft.Web/staticSites/customDomains` in `main.bicep` after the DNS record exists.
