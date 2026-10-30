# Infrastructure

The web app needs one Azure resource: a Static Web App on the Free plan in West Europe ([`main.bicep`](main.bicep)). The app runs fully in the browser (see [docs/decisions/0001-stack.md](../docs/decisions/0001-stack.md)). The hosted scan server and the database are below.

Static Web Apps is only offered in a few regions, and West Europe is the only EU one, so Sweden Central is not an option for this resource. Static content is served from the global edge; the region holds the app's metadata and, later, its managed Functions.

## Files

| File | What it is |
| --- | --- |
| [`main.bicep`](main.bicep), [`main.bicepparam`](main.bicepparam) | The app's infrastructure. CI provisions it on every push to `main`. |
| [`bootstrap.bicep`](bootstrap.bicep) | One-time CI identity: a user-assigned managed identity with GitHub OIDC federated credentials (`main` and pull requests) and Contributor on the resource group only. |
| [`bootstrap.sh`](bootstrap.sh) | Runs the one-time setup below. |
| [`scan.bicep`](scan.bicep) | The hosted scan server (decision record [0014](../docs/decisions/0014-hosted-scan-server.md)): a Consumption-only Container Apps environment and the app, in `rg-motifflab`. Deployed by the `Scan server` workflow, not by CI. |
| [`azure/setup.ps1`](azure/setup.ps1), [`azure/MotiffAzure.psm1`](azure/MotiffAzure.psm1) | One-time, in the subscription with `rg-shared`: the group `rg-motifflab` with its CI identity, Motiff Lab's database in the shared free-tier Cosmos DB account, the API's identity with access to that database only, the players' sign-in tenant, the repository variables, then the first deploys. |
| [`api.bicep`](api.bicep) | The Motiff Lab API (`src/MotiffLab.Api`) next to the scan server, signing in to Cosmos DB as `id-motifflab-api`. Deployed by the `API` workflow once sign-in and the database are set up. |

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

## The scan server, the API and the database

These live apart from the web app: in the subscription that holds `rg-shared` and its free-tier Cosmos DB account, in a resource group also named `rg-motifflab` (Poland Central), so the API reaches its database with its own identity and no keys. The two groups share a name but not a subscription. The services sign in to Azure with their own CI identity, through the `AZURE_SERVICES_*` repository variables; the web app's `AZURE_*` variables keep pointing at the web app's group.

The scan server (`services/omr`) is published and deployed by [`.github/workflows/scan-server.yml`](../.github/workflows/scan-server.yml) whenever `services/omr` or `scan.bicep` changes on `main`, or when run by hand from the Actions tab. It builds the image (about 2 GB, longer than CI's 5 minutes, hence its own workflow), pushes it to `ghcr.io/andreykukalev/motiff-omr`, and deploys `scan.bicep`; the job summary shows the server's address. The API's [`.github/workflows/api.yml`](../.github/workflows/api.yml) does the same for `src/MotiffLab.Api` and `api.bicep`. Until the setup below has run, both only publish their images.

Nothing here has a standing cost: the Consumption-only environment has no load balancer and no Log Analytics workspace, the apps scale to zero, the database stays within the free tier, and External ID is free for the first 50,000 monthly active users.

## Setting up the services and sign-in, once

On your own computer, in a clone of this repository, with the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) 2.60 or later and the [GitHub CLI](https://cli.github.com) (on Windows: `winget install Microsoft.AzureCLI GitHub.cli`), in PowerShell (Windows PowerShell 5.1 or PowerShell 7):

```powershell
git pull
.\infra\azure\setup.ps1
```

If Windows refuses to run scripts, start it with `powershell -ExecutionPolicy Bypass -File .\infra\azure\setup.ps1`. The script signs in where it needs to (a browser window opens: choose the account that owns *[Andrey Kukalev] Visual Studio Premium*, not a work account), shows what it will create, and asks once. Then:

1. it adds up the RU/s the shared account's databases already use, and stops without creating anything if Motiff Lab's 400 RU/s would go past the free 1000; otherwise it creates the database `motifflab` with the container `projects` (partition `/ownerId`). It changes nothing on the account itself or its other databases;
2. creates `rg-motifflab` with the CI identity `id-motifflab-github` ([`bootstrap.bicep`](bootstrap.bicep), Contributor on that group only) and `id-motifflab-api`, with data access to the `motifflab` database only;
3. creates the sign-in tenant (Microsoft Entra External ID, `motifflab.onmicrosoft.com`, Europe, 5 to 30 minutes), linked to `rg-motifflab`, signs in to it (a second browser window), and registers *Motiff Lab API* (scopes `Projects.ReadWrite` and `Scan`, v2 tokens) and *Motiff Lab* (the web app, with the redirect URIs of the Static Web App and of `dotnet run`), consents for every player, and creates the flow *Motiff Lab sign-in* (sign up and sign in with a code sent by email). If the Azure CLI may not create flows in the tenant, it says how to make that one in the Entra admin center, and waits;
4. sets the repository variables (identifiers, not secrets): `AZURE_SERVICES_*`, `COSMOS_ENDPOINT`, `AUTH_AUTHORITY`, `AUTH_AUDIENCE`, `AUTH_CLIENT_ID`, then `SCAN_URL` and `API_URL`;
5. runs the `Scan server`, `API` and `CI` workflows and waits for each. GitHub makes a new package private, Container Apps pulls without credentials, and GitHub has no command to change a package's visibility: the first time, the script opens the package's settings page (*Change visibility*, *Public*), waits, and re-runs the workflow.

CI then builds the web app with the sign-in settings from those variables (it writes `wwwroot/appsettings.json` before publishing; the committed file stays empty, so pull request previews and local builds have no accounts unless you fill it in). Running the script again is safe: it finds what is there and adds only what is missing. `-SkipDeploy` stops before the workflows; `-Domain <name>` picks another sign-in domain if `motifflab` is taken.

Google and Apple sign-in are added later, by hand, as identity providers ticked in the flow: [Google](https://learn.microsoft.com/entra/external-id/customers/how-to-google-federation-customers) needs an OAuth client in Google Cloud; [Apple](https://learn.microsoft.com/entra/external-id/customers/how-to-apple-federation-customers) needs the Apple Developer Program, and its key must be renewed every 6 months. A pull request preview signs in only once its address is added to *Motiff Lab*'s redirect URIs (Entra takes no wildcards); the iOS app gets its own registration later.

## Changes later

- Change infrastructure by editing `main.bicep`; the PR shows the `what-if`, and merging applies it.
- The CI identity is Contributor on the resource group, which includes the identity itself. That is acceptable for a single-owner project; move it to its own resource group if more people get write access to the repository.
- To rotate the deployment token: `az staticwebapp secrets reset-api-key --name swa-motifflab --resource-group rg-motifflab`. Nothing else needs to change.
- To add a custom domain, use `Microsoft.Web/staticSites/customDomains` in `main.bicep` after the DNS record exists.
