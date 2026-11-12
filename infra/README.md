# Infrastructure

The web app needs one Azure resource: a Static Web App on the Free plan in East US 2 ([`main.bicep`](main.bicep)), `swa-motifflab` in `rg-motifflab`, next to the scan server, the API and their database. The app runs fully in the browser (see [docs/decisions/0001-stack.md](../docs/decisions/0001-stack.md)). The hosted scan server and the database are below.

Static Web Apps is only offered in a few regions. West Europe, the only EU one, stopped accepting new Static Web Apps in 2026-11 (`RequestDisallowedByAzure`), so the app is in East US 2. Static content is served from the global edge; the region holds the app's metadata and, later, its managed Functions.

## Files

| File | What it is |
| --- | --- |
| [`main.bicep`](main.bicep), [`main.bicepparam`](main.bicepparam) | The app's infrastructure. CI provisions it on every push to `main`. |
| [`bootstrap.bicep`](bootstrap.bicep) | One-time CI identity: a user-assigned managed identity with GitHub OIDC federated credentials (`main` and pull requests) and Contributor on the resource group only. |
| [`bootstrap.sh`](bootstrap.sh) | The first setup, from when the web app lived alone in another subscription. Replaced by `azure/setup.ps1`. |
| [`scan.bicep`](scan.bicep) | The hosted scan server (decision record [0014](../docs/decisions/0014-hosted-scan-server.md)): a Consumption-only Container Apps environment and the app, in `rg-motifflab`. Deployed by the `Scan server` workflow, not by CI. |
| [`azure/ipad.ps1`](azure/ipad.ps1) | Lets the iPad app sign in, once: its MSAL redirect `msauth.com.andreykukalev.motifflab://auth` on the web app's registration *Motiff Lab*, with public client flows on. `setup.ps1` does the same for a new tenant. |
| [`azure/test-site-signin.ps1`](azure/test-site-signin.ps1) | Lets players sign in on the web test site (develop), once: its address as a single-page-app redirect on the web app's registration *Motiff Lab*. CI writes the same sign-in settings there as on the main address. |
| [`azure/pro.ps1`](azure/pro.ps1) | Gives a player Pro or makes them a tester: the API's app roles `Pro` and `Tester` in the sign-in tenant (0018 section 12). See [Pro and testers](#pro-and-testers). |
| [`azure/setup.ps1`](azure/setup.ps1), [`azure/MotiffAzure.psm1`](azure/MotiffAzure.psm1) | One-time, in the subscription with `rg-shared`: the group `rg-motifflab` with its CI identity, the web app's Static Web App, Motiff Lab's database, the API's identity with access to that database only, the players' sign-in tenant, the repository variables, then the first deploys. |
| [`api.bicep`](api.bicep) | The Motiff Lab API (`src/MotiffLab.Api`) next to the scan server, signing in to Cosmos DB as `id-motifflab-api`. Deployed by the `API` workflow once sign-in and the database are set up. |
| [`dev-env.bicep`](dev-env.bicep) | The develop environment's own servers and data, in `rg-motifflab-dev`: the Container Apps environment `cae-motifflab-dev`, a serverless Cosmos DB account with the same database and container, the API's identity `id-motifflab-api-dev` (that database only) and the CI identity `id-motifflab-github-dev` (Contributor on this group only, signing in from `develop`). See [The develop environment](#the-develop-environment). |
| [`azure/dev-env.ps1`](azure/dev-env.ps1) | Sets up the develop environment, once: creates `rg-motifflab-dev`, deploys `dev-env.bicep` there and sets the repository variables `AZURE_DEV_CLIENT_ID`, `AZURE_DEV_RESOURCE_GROUP`, `COSMOS_ENDPOINT_DEV`, `API_URL_DEV` and `SCAN_URL_DEV`. Changes nothing in `rg-motifflab`. Running it again is safe. |

## How deploys work

The `CI` workflow ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) builds and tests, publishes the web app, then signs in to Azure through OIDC as the bootstrap identity. No Azure secret is stored in GitHub: the Static Web Apps deployment token is read at run time.

- **Push to `main`**: deploys `main.bicep` (incremental), then deploys the app to production.
- **Push to `develop`**: deploys the app to the Static Web App's environment `develop`, the web test site, with develop's own API and scan server ([below](#the-develop-environment)).
- **Pull request**: builds and tests only. There are no preview sites (Andrei, 2026-10-31): a preview can't sign in, and the iPad app opens the production site, so a change is checked there after it is merged.
- Until the bootstrap has set the `AZURE_*` repository variables, the deploy steps are skipped.

## One-time setup

`azure/setup.ps1` sets up everything, the web app included: see [Setting up the services and sign-in, once](#setting-up-the-services-and-sign-in-once). Until 2026-10-30 the web app lived alone in the *Visual Studio Professional* subscription, set up by `bootstrap.sh`; Azure cannot move a resource to a subscription of another tenant, so `setup.ps1` creates a new Static Web App in `rg-motifflab`, with a new address, and points CI's `AZURE_*` variables at it. The old site stops getting deploys and can then be deleted.

## The scan server, the API and the database

These live with the web app in the subscription that holds `rg-shared`, in the resource group `rg-motifflab` (Poland Central), so the API reaches its database with its own identity and no keys. Everything signs in to Azure as the one CI identity: the services through the `AZURE_SERVICES_*` repository variables, CI's web app deploys through `AZURE_*`, both set to that group by the setup script.

The scan server (`services/omr`) is published and deployed by [`.github/workflows/scan-server.yml`](../.github/workflows/scan-server.yml) whenever `services/omr` or `scan.bicep` changes on `main`, or when run by hand from the Actions tab; from `main` it waits for Andrei's approval (the Environment `production`). The same workflow deploys develop's copy from `develop` ([below](#the-develop-environment)). It builds the image (about 2 GB, longer than CI's 5 minutes, hence its own workflow), pushes it to `ghcr.io/andreykukalev/motiff-omr`, and deploys `scan.bicep`; the job summary shows the server's address. The API's [`.github/workflows/api.yml`](../.github/workflows/api.yml) does the same for `src/MotiffLab.Api` and `api.bicep`. Until the setup below has run, both only publish their images.

Nothing here has a standing cost: the Consumption-only environment has no load balancer and no Log Analytics workspace, the apps scale to zero, the database stays within the free tier, and External ID is free for the first 50,000 monthly active users.

## The develop environment

A version is tried on the web test site and on the iPad before it reaches `main`, so it has its own servers and data in their own group, `rg-motifflab-dev`, in the same subscription and region (Andrei chose a separate group, 2026-11-11; [0014](../docs/decisions/0014-hosted-scan-server.md), item 15):

| `rg-motifflab-dev` | `rg-motifflab` (main) |
| --- | --- |
| Container Apps environment `cae-motifflab-dev` | `cae-motifflab` |
| API `ca-motifflab-api-dev`, as `id-motifflab-api-dev` | `ca-motifflab-api`, as `id-motifflab-api` |
| Scan server `ca-motifflab-scan-dev` | `ca-motifflab-scan` |
| Serverless Cosmos DB `cosmos-motifflab-dev-<8 characters>`, database `motifflab`, container `projects` | the players' real database |
| CI identity `id-motifflab-github-dev`: signs in from `develop`, Contributor on this group only | `id-motifflab-github` |

Shared by both: the sign-in tenant (the same accounts and the roles Pro and Tester), the Static Web App (its environment `develop` is the web test site) and the images on `ghcr.io` (one package each, tagged by commit; the newest also `latest` from `main`, `develop` from `develop`).

- **Set up once** by Andrei: `.\infra\azure\dev-env.ps1` (as `setup.ps1`, in PowerShell with the Azure and GitHub CLIs). It creates the group, deploys [`dev-env.bicep`](dev-env.bicep) and sets `AZURE_DEV_CLIENT_ID`, `AZURE_DEV_RESOURCE_GROUP`, `COSMOS_ENDPOINT_DEV`, `API_URL_DEV` and `SCAN_URL_DEV` (`https://ca-motifflab-api-dev.<environment's domain>` and the same for the scan server).
- **Deploys**: a push to `develop` that changes the API (or music-core) or the scan server runs the `API` or `Scan server` workflow, which deploys the same `api.bicep` or `scan.bicep` with `branch=develop` (the names above) into `rg-motifflab-dev`, at once and without approval, signed in as `id-motifflab-github-dev`; *Run workflow* on `develop` does the same by hand. Until `AZURE_DEV_CLIENT_ID` is set, those runs only say so and deploy nothing. `main` deploys exactly as before, after Andrei's approval.
- **The web test site** gets `API_URL_DEV` and `SCAN_URL_DEV` in its `appsettings.json` (CI, on every deploy from `develop`), or main's addresses while they are not set. The develop servers allow the test site's origin (`<name>-develop.<region>.<n>.azurestaticapps.net`, the same rule as main's).
- **The iPad** knows both environments: `ipad/App/MotiffLab/Account.json` has main's servers (`Account`) and develop's (`Environments.Test`, the same addresses as `API_URL_DEV` and `SCAN_URL_DEV`: change them there if those change). A version build (`ipad/build-version.sh v0.<n>`, a tag not in `main` yet) starts in Test, a version in `main` in Main, and Settings → Account → «Окружение: Основное | Тестовое» switches, shown only in a version build or to a tester. Each environment has its own library on the iPad (Main's in Documents, as before, so the pieces already there stay Main's; Test's in `Documents/Test`, with its own sync records), Test is marked «Тест», and in a version build Main is read only until «Разрешить запись в Основное» is turned on for that build ([ipad/README.md](../ipad/README.md#main-and-test-environments)).
- **The data**: pieces saved on the test site or a version build live in the test database, never next to the players' real data. They reach the main database only through the released version or an export. Nothing cleans the test database without asking Andrei.
- **Cost**: none standing. Both apps scale to zero; the serverless account bills only requests and storage (cents a month at test use); Container Apps' free grant is per subscription.
- **Remove it all** at once: `az group delete --name rg-motifflab-dev`, then delete the five `*_DEV*` repository variables, and the test site falls back to main's servers; remove `Environments` from the iPad's `Account.json` and its builds know only Main.

## Setting up the services and sign-in, once

On your own computer, in a clone of this repository, with the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) 2.60 or later and the [GitHub CLI](https://cli.github.com) (on Windows: `winget install Microsoft.AzureCLI GitHub.cli`), in PowerShell (Windows PowerShell 5.1 or PowerShell 7):

```powershell
git pull
.\infra\azure\setup.ps1
```

If Windows refuses to run scripts, start it with `powershell -ExecutionPolicy Bypass -File .\infra\azure\setup.ps1`. The script signs in where it needs to (a browser window opens: choose the account that owns *[Andrey Kukalev] Visual Studio Premium*, not a work account), shows what it will create, and asks once. Then:

1. it adds up the RU/s the shared account's databases already use. If Motiff Lab's 400 RU/s fit in the free 1000, it creates the database `motifflab` with the container `projects` (partition `/ownerId`) there, changing nothing on the account itself or its other databases. If they don't (cix's two databases take 800), it creates Motiff Lab's own serverless account `cosmos-motifflab-<subscription prefix>` in `rg-motifflab` instead, with the same database and container: no reserved RU/s, billed only for requests and storage (cents a month at this use; Andrei's choice, 2026-10-30). A later run finds that account and keeps it;
2. creates `rg-motifflab` with the CI identity `id-motifflab-github` ([`bootstrap.bicep`](bootstrap.bicep), Contributor on that group only), `id-motifflab-api`, with data access to the `motifflab` database only, and the web app's Static Web App `swa-motifflab` ([`main.bicep`](main.bicep), Free plan), whose address it keeps as `WEB_HOST`;
3. creates the sign-in tenant (Microsoft Entra External ID, `motifflab.onmicrosoft.com`, Europe, 5 to 30 minutes), linked to `rg-motifflab`, signs in to it (a second browser window), and registers *Motiff Lab API* (scopes `Projects.ReadWrite` and `Scan`, v2 tokens) and *Motiff Lab* (the web app, with the redirect URIs of the Static Web App and of `dotnet run`), consents for every player, and creates the flow *Motiff Lab sign-in* (sign up and sign in with a code sent by email). If the Azure CLI may not create flows in the tenant, it says how to make that one in the Entra admin center, and waits;
4. sets the repository variables (identifiers, not secrets): `AZURE_*` and `AZURE_SERVICES_*`, `WEB_HOST`, `COSMOS_ENDPOINT`, `AUTH_AUTHORITY`, `AUTH_AUDIENCE`, `AUTH_CLIENT_ID`, then `SCAN_URL` and `API_URL`;
5. runs the `Scan server`, `API` and `CI` workflows and waits for each. GitHub makes a new package private, Container Apps pulls without credentials, and GitHub has no command to change a package's visibility: the first time, the script opens the package's settings page (*Change visibility*, *Public*), waits, and re-runs the workflow.

CI then builds the web app with the sign-in settings from those variables (it writes `wwwroot/appsettings.json` before publishing; the committed file stays empty, so pull request builds and local builds have no accounts unless you fill it in). Running the script again is safe: it finds what is there and adds only what is missing. `-SkipDeploy` stops before the workflows; `-Domain <name>` picks another sign-in domain if `motifflab` is taken.

Google and Apple sign-in are added later, by hand, as identity providers ticked in the flow: [Google](https://learn.microsoft.com/entra/external-id/customers/how-to-google-federation-customers) needs an OAuth client in Google Cloud; [Apple](https://learn.microsoft.com/entra/external-id/customers/how-to-apple-federation-customers) needs the Apple Developer Program, and its key must be renewed every 6 months. The iPad app signs in with the web app's registration on its own iOS redirect, so it has the same scopes, consent and roles: run `.\infra\azure\ipad.ps1` once for a tenant set up before it.

## Pro and testers

What runs on the device is free; scanning and transcription on Motiff Lab's server, saving online and the assistant are Pro ([0018](../docs/decisions/0018-redesign.md), section 12). A player's plan is an app role of *Motiff Lab API* in the sign-in tenant, so it travels in their access token and the API and the scan server check it on every request without a database (`MotiffLab.MusicCore.Accounts.Plans`, `services/omr/app/auth.py`): `Pro` allows those features; `Tester` may also switch between Pro and no Pro in Settings to test both, sent as the `Motiff-Plan` header, which the servers trust only from a tester. Reading, listing and erasing projects saved online need no Pro, so a player who leaves Pro keeps their data. Until payment exists, Pro is given with:

```powershell
.\infra\azure\pro.ps1 -Email you@example.com -Pro -Tester   # adds the two roles the first time, then assigns them
.\infra\azure\pro.ps1 -List
```

The player must have signed in once. The role reaches them at their next sign-in (or within an hour). `-Remove` takes a role away.

## Changes later

- Change infrastructure by editing `main.bicep`; the PR shows the `what-if`, and merging applies it.
- The CI identity is Contributor on the resource group, which includes the identity itself. That is acceptable for a single-owner project; move it to its own resource group if more people get write access to the repository.
- To rotate the deployment token: `az staticwebapp secrets reset-api-key --name swa-motifflab --resource-group rg-motifflab`. Nothing else needs to change.
- To add a custom domain, use `Microsoft.Web/staticSites/customDomains` in `main.bicep` after the DNS record exists.
