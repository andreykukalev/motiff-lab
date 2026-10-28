# Infrastructure

The web app needs one Azure resource: a Static Web App on the Free plan in West Europe ([`main.bicep`](main.bicep)). The app runs fully in the browser (see [docs/decisions/0001-stack.md](../docs/decisions/0001-stack.md)). The hosted scan server and the database are below.

Static Web Apps is only offered in a few regions, and West Europe is the only EU one, so Sweden Central is not an option for this resource. Static content is served from the global edge; the region holds the app's metadata and, later, its managed Functions.

## Files

| File | What it is |
| --- | --- |
| [`main.bicep`](main.bicep), [`main.bicepparam`](main.bicepparam) | The app's infrastructure. CI provisions it on every push to `main`. |
| [`bootstrap.bicep`](bootstrap.bicep) | One-time CI identity: a user-assigned managed identity with GitHub OIDC federated credentials (`main` and pull requests) and Contributor on the resource group only. |
| [`bootstrap.sh`](bootstrap.sh) | Runs the one-time setup below. |
| [`scan.bicep`](scan.bicep) | The hosted scan server (decision record [0014](../docs/decisions/0014-hosted-scan-server.md)): a Consumption-only Container Apps environment and the app, in Poland Central. Deployed by the `Scan server` workflow, not by CI. |
| [`cosmos-bootstrap.sh`](cosmos-bootstrap.sh) | One-time: Motiff Lab's database in the shared free-tier Cosmos DB account, and the API's identity with access to that database only. |

## How deploys work

The `CI` workflow ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) builds and tests, publishes the web app, then signs in to Azure through OIDC as the bootstrap identity. No Azure secret is stored in GitHub: the Static Web Apps deployment token is read at run time.

- **Push to `main`**: deploys `main.bicep` (incremental), then deploys the app to production.
- **Pull request** from this repository: shows a `what-if` of `main.bicep` in the log, deploys a preview environment and comments its URL on the PR. Closing the PR deletes the preview.
- Until the bootstrap has set the `AZURE_*` repository variables, and on forks, the deploy jobs are skipped.

The Free plan allows 3 preview environments at a time. A fourth open PR fails its deploy until an older PR is closed.

## One-time setup

Requires the Azure CLI (2.53 or later) signed in with rights to create role assignments on the new resource group (Owner on the subscription, as on a Visual Studio subscription), and the GitHub CLI signed in to this repository.

```bash
az login
az account set --subscription "<subscription name or id>"
gh auth login
./infra/bootstrap.sh
```

The script asks for confirmation, then:

1. creates the resource group `rg-motifflab` in West Europe,
2. deploys `bootstrap.bicep` (CI identity, federated credentials, role assignment),
3. deploys `main.bicep` once, so pull requests have a site to deploy previews to,
4. sets the repository variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and `AZURE_RESOURCE_GROUP` (identifiers, not secrets).

Then re-run CI on an open PR, or push to `main`, for the first app deploy. Re-running the script is safe.

## The scan server and the database

The scan server (`services/omr`) is published and deployed by [`.github/workflows/scan-server.yml`](../.github/workflows/scan-server.yml) whenever `services/omr` or `scan.bicep` changes on `main`, or when run by hand from the Actions tab. It builds the image (about 2 GB, longer than CI's 5 minutes, hence its own workflow), pushes it to `ghcr.io/andreykukalev/motiff-omr`, and deploys `scan.bicep` as the CI identity; the job summary shows the server's address.

Nothing here has a standing cost: the Consumption-only environment has no load balancer and no Log Analytics workspace, and the app scales to zero, with one copy at most.

Once, after the first run of the workflow:

1. **Make the image public.** GitHub creates a new package as private, and Container Apps pulls without credentials. On GitHub open your profile's **Packages**, then **motiff-omr**, **Package settings**, **Change visibility**, **Public**. Then re-run the `Scan server` workflow, which stops with this message until then.
2. **Create the database.** Signed in to the subscription with `rg-shared`:

   ```bash
   az login
   az account set --subscription "<subscription name or id>"
   ./infra/cosmos-bootstrap.sh
   ```

   It finds the free-tier account in `rg-shared`, adds up the RU/s its databases already use, and stops without creating anything if Motiff Lab's 400 RU/s would go past the free 1000. Otherwise it asks, then creates the database `motifflab` with the container `projects`, and the identity `id-motifflab-api` in `rg-motifflab` with data access to that database only. It does not change the account or its other databases. Re-running it is safe.

## Changes later

- Change infrastructure by editing `main.bicep`; the PR shows the `what-if`, and merging applies it.
- The CI identity is Contributor on the resource group, which includes the identity itself. That is acceptable for a single-owner project; move it to its own resource group if more people get write access to the repository.
- To rotate the deployment token: `az staticwebapp secrets reset-api-key --name swa-motifflab --resource-group rg-motifflab`. Nothing else needs to change.
- To add a custom domain, use `Microsoft.Web/staticSites/customDomains` in `main.bicep` after the DNS record exists.
