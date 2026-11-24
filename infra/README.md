# Infrastructure

**Status (2026-11-16):** the web app is archived ([0032](../docs/architecture/adr/0032-ipad-first-web-archived.md)). Its Static Web App `swa-motifflab` (Free plan, with the environment `develop`) is kept but no longer updated: CI publishes, provisions `main.bicep` and deploys only while the repository variable `WEB_ACTIVE` is `true`, and it is not set. Everything else here (sign-in, the API, the databases, the scan servers, the develop environment, the CI identities) is shared with the iPad app and in service. `develop.ps1` and `test-site-signin.ps1` belong to the archived web. Web-only and shared components: 0032, "Infrastructure".

The web app needs one Azure resource: a Static Web App on the Free plan in East US 2 ([`main.bicep`](main.bicep)), `swa-motifflab` in `rg-motifflab`, next to the scan server, the API and their database. The app runs fully in the browser (see [docs/architecture/adr/0001-stack.md](../docs/architecture/adr/0001-stack.md)). The hosted scan server and the database are below.

Static Web Apps is only offered in a few regions. West Europe, the only EU one, stopped accepting new Static Web Apps in 2026-11 (`RequestDisallowedByAzure`), so the app is in East US 2. Static content is served from the global edge; the region holds the app's metadata and, later, its managed Functions.

## Files

| File | What it is |
| --- | --- |
| [`main.bicep`](main.bicep), [`main.bicepparam`](main.bicepparam) | The app's infrastructure. CI provisions it on every push to `main`. |
| [`bootstrap.bicep`](bootstrap.bicep) | One-time CI identity: a user-assigned managed identity with GitHub OIDC federated credentials (`main` and pull requests) and Contributor on the resource group only. |
| [`bootstrap.sh`](bootstrap.sh) | The first setup, from when the web app lived alone in another subscription. Replaced by `azure/setup.ps1`. |
| [`scan.bicep`](scan.bicep) | The hosted scan server (decision records [0014](../docs/architecture/adr/0014-hosted-scan-server.md), [0035](../docs/architecture/adr/0035-scan-jobs-four-engines.md)): the app, 4 vCPU and 8 GiB, scaled on HTTP requests and on its job queue, in `rg-motifflab`. Deployed by the `Scan server` workflow, not by CI. |
| [`scan-base.bicep`](scan-base.bicep) | What the scan server stands on, made once by Andrei: its own Container Apps environment `cae-motifflab-scan` (workload profiles, the Consumption profile only), its identity `id-motifflab-scan`, the job storage (account `stmlscan<13 characters>`, queue `scan-units`, container `scan-jobs`, a rule deleting job blobs after a day) and the identity's roles on that queue and container. See [Scan jobs](#scan-jobs). |
| [`azure/ipad.ps1`](azure/ipad.ps1) | Lets the iPad app sign in, once: its MSAL redirect `msauth.com.andreykukalev.motifflab://auth` on the web app's registration *Motiff Lab*, with public client flows on. `setup.ps1` does the same for a new tenant. |
| [`azure/test-site-signin.ps1`](azure/test-site-signin.ps1) | Lets players sign in on the web test site (develop), once: its address as a single-page-app redirect on the web app's registration *Motiff Lab*. CI writes the same sign-in settings there as on the main address. |
| [`azure/pro.ps1`](azure/pro.ps1) | Gives a player Pro or makes them a tester: the API's app roles `Pro` and `Tester` in the sign-in tenant (0018 section 12). See [Pro and testers](#pro-and-testers). |
| [`azure/setup.ps1`](azure/setup.ps1), [`azure/MotiffAzure.psm1`](azure/MotiffAzure.psm1) | One-time, in the subscription with `rg-shared`: the group `rg-motifflab` with its CI identity, the web app's Static Web App, Motiff Lab's database, the API's identity with access to that database only, the players' sign-in tenant, the repository variables, then the first deploys. |
| [`api.bicep`](api.bicep) | The Motiff Lab API (`src/backend/MotiffLab.Api`) in its Consumption-only environment `cae-motifflab`, signing in to Cosmos DB as `id-motifflab-api`. Deployed by the `API` workflow once sign-in and the database are set up. |
| [`dev-env.bicep`](dev-env.bicep) | The develop environment's own servers and data, in `rg-motifflab-dev`: the API's Container Apps environment `cae-motifflab-dev`, the scan server's base (`scan-base.bicep` with `branch=develop`), a serverless Cosmos DB account with the same database and container, the API's identity `id-motifflab-api-dev` (that database only) and the CI identity `id-motifflab-github-dev` (Contributor on this group only, signing in from `develop`). See [The develop environment](#the-develop-environment). |
| [`azure/dev-env.ps1`](azure/dev-env.ps1) | Sets up the develop environment, once: creates `rg-motifflab-dev`, deploys `dev-env.bicep` there and sets the repository variables `AZURE_DEV_CLIENT_ID`, `AZURE_DEV_RESOURCE_GROUP`, `COSMOS_ENDPOINT_DEV`, `API_URL_DEV` and `SCAN_URL_DEV`. Changes nothing in `rg-motifflab`. Running it again is safe. |
| [`azure/reset-dev-data.ps1`](azure/reset-dev-data.ps1) | The spare for all players' data: empties the develop environment's whole database (deletes and recreates the container `projects` in `cosmos-motifflab-dev-*` with the same settings). Refuses any other account or group. The normal way is the apps' «Очистить всё»: see [A fresh testing cycle](#a-fresh-testing-cycle). |

## How deploys work

The `CI` workflow ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) builds and tests, publishes the web app, then signs in to Azure through OIDC as the bootstrap identity. No Azure secret is stored in GitHub: the Static Web Apps deployment token is read at run time.

- **Push to `main`**: deploys `main.bicep` (incremental), then deploys the app to production.
- **Push to `develop`**: deploys the app to the Static Web App's environment `develop`, the web test site, with develop's own API and scan server ([below](#the-develop-environment)).
- **Pull request**: builds and tests only. There are no preview sites (Andrei, 2026-10-31): a preview can't sign in, and the iPad app opens the production site, so a change is checked there after it is merged.
- Until the bootstrap has set the `AZURE_*` repository variables, the deploy steps are skipped.
- **While the web is archived** (0032): none of the above deploys. Publishing, `main.bicep` and the Static Web Apps deploy run only when the repository variable `WEB_ACTIVE` is `true`; Andrei sets it when he reactivates the web (`docs/web-archive.md`). The `API` and `Scan server` workflows deploy as before.

## One-time setup

`azure/setup.ps1` sets up everything, the web app included: see [Setting up the services and sign-in, once](#setting-up-the-services-and-sign-in-once). Until 2026-10-30 the web app lived alone in the *Visual Studio Professional* subscription, set up by `bootstrap.sh`; Azure cannot move a resource to a subscription of another tenant, so `setup.ps1` creates a new Static Web App in `rg-motifflab`, with a new address, and points CI's `AZURE_*` variables at it. The old site stops getting deploys and can then be deleted.

## The scan server, the API and the database

These live with the web app in the subscription that holds `rg-shared`, in the resource group `rg-motifflab` (Poland Central), so the API reaches its database with its own identity and no keys. Everything signs in to Azure as the one CI identity: the services through the `AZURE_SERVICES_*` repository variables, CI's web app deploys through `AZURE_*`, both set to that group by the setup script.

The scan server (`src/backend/omr`) is published and deployed by [`.github/workflows/scan-server.yml`](../.github/workflows/scan-server.yml) whenever `src/backend/omr` or `scan.bicep` changes on `main`, or when run by hand from the Actions tab; from `main` it waits for Andrei's approval (the Environment `production`). The same workflow deploys develop's copy from `develop` ([below](#the-develop-environment)). It builds the image (about 2 GB, longer than CI's 5 minutes, hence its own workflow), pushes it to `ghcr.io/andreykukalev/motiff-omr`, and deploys `scan.bicep`; the job summary shows the server's address. The API's [`.github/workflows/api.yml`](../.github/workflows/api.yml) does the same for `src/backend/MotiffLab.Api` and `api.bicep`. Until the setup below has run, both only publish their images.

Nothing here has a standing cost: the environments have no load balancer, no dedicated workload profile and no Log Analytics workspace, the apps scale to zero, the job storage holds a day of scans at most (cents a month), the database stays within the free tier, and External ID is free for the first 50,000 monthly active users.

### Scan jobs

From version 1.3 the scan server reads scans as jobs, each page by four engines side by side ([0035](../docs/architecture/adr/0035-scan-jobs-four-engines.md)), so it needs 4 vCPU and 8 GiB a copy and somewhere to keep the jobs:

- **4 vCPU and 8 GiB** are what the Consumption profile of a *workload profiles* environment gives an app, with images up to 8 GB a replica (the server's is about 6.7 GB); a Consumption-only environment such as `cae-motifflab` stops at 2 vCPU and 4 GiB, and an environment cannot change its type. So the scan server moves to its own environment, `cae-motifflab-scan`; the API stays in `cae-motifflab`. Without a dedicated profile there is no plan management charge: the environment costs nothing by itself.
- **The jobs** lie in a storage account of their own: the units' messages in the queue `scan-units`, the pages, units and readings in the container `scan-jobs`. The server signs in as `id-motifflab-scan`, which may read and write that queue and that container only (Storage Queue Data Contributor, Storage Blob Data Contributor); the account takes no keys at all. The server deletes a job 10 minutes after its result was fetched and a day after it was made at the latest; a lifecycle rule deletes any job blob not written for a day, as a backstop (Azure runs it about once a day).
- **Scale:** besides the HTTP rule, a rule on the queue's length (as `id-motifflab-scan` too) wakes a copy while any unit waits, is read or waits for a retry, one more copy for each 4 units, up to `maxReplicas`; once every job is finished the app scales to zero.
- **Why the base is its own file:** the `Scan server` workflow signs in as a Contributor, which may not give roles; Andrei's own sign-in makes the base once, and `scan.bicep` only finds it.

**Once, in this order** (by Andrei; the players' cloud server changes only with the release to `main`). The `Scan server` workflow change that waits for the base is merged first, or with the server's change. Until the base exists in a group, the workflow deploys nothing there and says so.

develop (`rg-motifflab-dev`):

1. `.\infra\azure\dev-env.ps1`: deploys the base as part of `dev-env.bicep` and sets `SCAN_URL_DEV` to the new address.
2. `az containerapp delete --resource-group rg-motifflab-dev --name ca-motifflab-scan-dev --yes`: the old app lives in `cae-motifflab-dev`, and an app cannot move to another environment. It keeps nothing: `/recognise` deletes each upload when it answers.
3. `gh workflow run scan-server.yml --ref develop` (or *Run workflow* on `develop`): the run's summary shows the address.
4. `Environments.Test.Scan` in `src/ipad/App/MotiffLab/Account.json` becomes that address.

The release (`rg-motifflab`):

1. Before the release pull request: `az deployment group create --resource-group rg-motifflab --name scan-base --template-file infra/scan-base.bicep` (new setups: `setup.ps1` does it).
2. Right before approving the `Scan server` deploy from `main`: `az containerapp delete --resource-group rg-motifflab --name ca-motifflab-scan --yes`; then approve.
3. `gh variable set SCAN_URL --body <the address in the run's summary>`, and `Account.Scan` in `Account.json` becomes it. iPad builds with the old address cannot scan until they are updated.

## The develop environment

A version is tried on the web test site and on the iPad before it reaches `main`, so it has its own servers and data in their own group, `rg-motifflab-dev`, in the same subscription and region (Andrei chose a separate group, 2026-11-11; [0014](../docs/architecture/adr/0014-hosted-scan-server.md), item 15):

| `rg-motifflab-dev` | `rg-motifflab` (main) |
| --- | --- |
| Container Apps environment `cae-motifflab-dev` | `cae-motifflab` |
| API `ca-motifflab-api-dev`, as `id-motifflab-api-dev` | `ca-motifflab-api`, as `id-motifflab-api` |
| Scan server `ca-motifflab-scan-dev` in `cae-motifflab-scan-dev`, as `id-motifflab-scan-dev`, its jobs in `stmlscan<13 characters>` | `ca-motifflab-scan` in `cae-motifflab-scan`, as `id-motifflab-scan` |
| Serverless Cosmos DB `cosmos-motifflab-dev-<8 characters>`, database `motifflab`, container `projects` | the players' real database |
| CI identity `id-motifflab-github-dev`: signs in from `develop`, Contributor on this group only | `id-motifflab-github` |

Shared by both: the sign-in tenant (the same accounts and the roles Pro and Tester), the Static Web App (its environment `develop` is the web test site) and the images on `ghcr.io` (one package each, tagged by commit; the newest also `latest` from `main`, `develop` from `develop`).

- **Set up once** by Andrei: `.\infra\azure\dev-env.ps1` (as `setup.ps1`, in PowerShell with the Azure and GitHub CLIs). It creates the group, deploys [`dev-env.bicep`](dev-env.bicep) and sets `AZURE_DEV_CLIENT_ID`, `AZURE_DEV_RESOURCE_GROUP`, `COSMOS_ENDPOINT_DEV`, `API_URL_DEV` and `SCAN_URL_DEV` (`https://ca-motifflab-api-dev.<environment's domain>`, and `https://ca-motifflab-scan-dev.<domain of cae-motifflab-scan-dev>`).
- **Deploys**: a push to `develop` that changes the API (or music-core) or the scan server runs the `API` or `Scan server` workflow, which deploys the same `api.bicep` or `scan.bicep` with `branch=develop` (the names above) into `rg-motifflab-dev`, at once and without approval, signed in as `id-motifflab-github-dev`; *Run workflow* on `develop` does the same by hand. Until `AZURE_DEV_CLIENT_ID` is set, those runs only say so and deploy nothing. `main` deploys exactly as before, after Andrei's approval.
- **The web test site** gets `API_URL_DEV` and `SCAN_URL_DEV` in its `appsettings.json` (CI, on every deploy from `develop`), or main's addresses while they are not set. The develop servers allow the test site's origin (`<name>-develop.<region>.<n>.azurestaticapps.net`, the same rule as main's).
- **The iPad** knows both environments: `src/ipad/App/MotiffLab/Account.json` has main's servers (`Account`) and develop's (`Environments.Test`, the same addresses as `API_URL_DEV` and `SCAN_URL_DEV`: change them there if those change). A version build (`src/ipad/build-version.sh v0.<n>`, a tag not in `main` yet) starts in Test, a version in `main` in Main, and Settings → Account → «Окружение: Основное | Тестовое» switches, shown only in a version build or to a tester. Each environment has its own library on the iPad (Main's in Documents, as before, so the pieces already there stay Main's; Test's in `Documents/Test`, with its own sync records), Test is marked «Тест», and in a version build Main is read only until «Разрешить запись в Основное» is turned on for that build ([src/ipad/README.md](../src/ipad/README.md#main-and-test-environments)).
- **The data**: pieces saved on the test site or a version build live in the test database, never next to the players' real data. They reach the main database only through the released version or an export. Nothing cleans the test database without asking Andrei; for a new testing cycle a player empties their own test data and every device's test library with «Очистить всё», or Andrei empties all of it with the spare script ([below](#a-fresh-testing-cycle)).
- **Cost**: none standing. Both apps scale to zero; the serverless account bills only requests and storage (cents a month at test use); Container Apps' free grant is per subscription.
- **Remove it all** at once: `az group delete --name rg-motifflab-dev`, then delete the five `*_DEV*` repository variables, and the test site falls back to main's servers; remove `Environments` from the iPad's `Account.json` and its builds know only Main.

### A fresh testing cycle

The test environment exists only for testing; the main one holds the real scores. To start a testing cycle from an empty library everywhere (Andrei, 2026-11-13), press one button on one device:

- **«Очистить всё»** ("Clear everything"): Settings → «О приложении» ("About"), on the iPad while it works in Test, or on the web test site (shown only on the test site's address). After a confirmation, signed in, it:
  1. empties the player's data in the test database: the API's `POST /me/reset` deletes their pieces and profile there and records a reset stamp in their partition (`src/backend/MotiffLab.Api/TestReset.cs`; a small document `player.reset` that no list shows as a piece). The endpoint exists only on develop's API, the one whose database is `cosmos-motifflab-dev-<8 characters>`; on main's it is 404;
  2. clears the test library on this device, and keeps the stamp as applied here;
  3. reaches the player's other devices by itself: develop's API sends the stamp with every list of projects (the header `Motiff-Reset`), and a device that sees a newer stamp than the one it applied clears its own test library before its sync sends or fetches anything. The iPad does it when the library opens, comes back to the screen, after a piece closes or on sign-in (in Test, signed in with the same account); the web test site when its library opens or the page comes back into view (signed in, saving online on), and then it loads again. A save from a device that has not applied the reset yet is refused (409, the code `reset`, from the header `Motiff-Reset-Seen`), so nothing from before the reset comes back. A device that is signed out, or in Main, hears nothing and keeps its library.

  What a device clears is what the button clears there. On the iPad: Test's project files (`Documents/Test/*.motiff.json`), its sync records (`sync-test.json`) and its folder-import records (`LibraryImport/test`: the folders added, the import job, the recognition batch, the playlists kept on the iPad and their copies); Main's library stays as it is. On the web: the site's own storage of the library in that browser (its IndexedDB stores and the library's entries in local storage); the interface language, the theme, the settings, the sign-in and the scan server address stay. Only the app's own files: the player's PDFs, recordings and folders added by link are never touched.

- **The spare, for all players at once**: `infra/azure/reset-dev-data.ps1` empties the whole test database, every tester's data, from Windows PowerShell in the clone (`C:\_DATA\Sources\GH\motiff-lab`), with the Azure CLI signed in as for `dev-env.ps1`:

  ```powershell
  .\infra\azure\reset-dev-data.ps1 -WhatIf   # shows what would go, changes nothing
  .\infra\azure\reset-dev-data.ps1           # asks "Empty the test database? [y/N]"; -Yes skips the question
  ```

  It finds the account by `dev-env.bicep`'s rules (the one `cosmos-motifflab-dev-<8 characters>` in `rg-motifflab-dev`, tagged `env=develop`) and stops before changing anything if what it finds is not that: another group, main's `cosmos-motifflab-<subscription>`, none or two. It shows about how many items the container `projects` of the database `motifflab` holds (every player's pieces and profiles; Azure Monitor's count, a few minutes behind), then deletes the container and creates it again, empty, with the settings it read from it just before: partition key `/ownerId` (the same key version), the same indexing policy, serverless. The API's data access is granted on the database, so it keeps working; nothing else changes. It tells no device: the reset stamps go with the container, so afterwards each player presses «Очистить всё» once on one device, and their other devices follow.

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

What runs on the device is free; scanning and transcription on Motiff Lab's server, saving online and the assistant are Pro ([0018](../docs/architecture/adr/0018-redesign.md), section 12). A player's plan is an app role of *Motiff Lab API* in the sign-in tenant, so it travels in their access token and the API and the scan server check it on every request without a database (`MotiffLab.MusicCore.Accounts.Plans`, `src/backend/omr/app/auth.py`): `Pro` allows those features; `Tester` may also switch between Pro and no Pro in Settings to test both, sent as the `Motiff-Plan` header, which the servers trust only from a tester. Reading, listing and erasing projects saved online need no Pro, so a player who leaves Pro keeps their data. Until payment exists, Pro is given with:

```powershell
.\infra\azure\pro.ps1 -Email you@example.com -Pro -Tester   # adds the two roles the first time, then assigns them
.\infra\azure\pro.ps1 -List
```

The player must have signed in once. The role reaches them at their next sign-in (or within an hour). `-Remove` takes a role away.

## The assistant's model

The assistant's relay (`POST /assistant/turn`, [0028](../docs/architecture/adr/0028-ai-bar-edits.md) section 4, `src/backend/MotiffLab.Api/Assistant`) is deployed with the API but calls no model until one is configured. Until then it answers "not configured", and the app sends no notes. To switch it on:

1. Choose the provider (decision 4 in `governance/README.md`). The API has two adapters (`src/backend/MotiffLab.Api/Assistant`): Claude through the official Anthropic SDK, for two providers, and a GPT model through the official OpenAI SDK on Azure OpenAI's Responses API. The provider is chosen by settings alone:
   - `anthropic`, the Claude API, billed by Anthropic. In the [Claude Console](https://platform.claude.com), create a workspace for Motiff Lab ([Create workspace](https://platform.claude.com/settings/workspaces/new)), give it a monthly limit of about $20 ([Spend limits](https://platform.claude.com/settings/spend-limits), *Workspace limits*; the Default Workspace takes no limit), add credits ([Billing](https://platform.claude.com/settings/billing)) and create an API key in that workspace ([API keys](https://platform.claude.com/settings/keys)).
   - `foundry`, Claude on Microsoft Foundry, billed through the Azure subscription (it needs a pay-as-you-go payment method; credit-only, free-trial and student subscriptions are refused). In [Microsoft Foundry](https://ai.azure.com/), open or create a project, then *Model catalog*, choose the Claude model, *Deploy*, *Custom settings* (steps: [Deploy and use Claude models in Microsoft Foundry](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/use-foundry-models-claude)). Note the resource's name (in its endpoint `https://<resource>.services.ai.azure.com/anthropic`), the deployment's name, and the API key (the resource's *Home* page or the deployment's *Details* tab). A spending cap is an Azure budget on the subscription ([Cost Management budgets](https://portal.azure.com/#view/Microsoft_Azure_CostManagement/Menu/~/budgets)); a budget alerts, it does not stop the calls.
   - `azure-openai`, a GPT model on Azure OpenAI in Microsoft Foundry, billed through the Azure subscription (credit subscriptions included). In [Microsoft Foundry](https://ai.azure.com/), open or create a project, then *Model catalog*, choose the model (recommended: `gpt-6.1-sol`), *Deploy*, deployment type *Global Standard* (steps: [Deploy Microsoft Foundry Models in the Foundry portal](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/deploy-foundry-models)). Note the resource's name (in its endpoint `https://<resource>.openai.azure.com`), the deployment's name, and the API key (the resource's *Home* page or the deployment's *Details* tab). The API calls the resource's v1 Responses API (`https://<resource>.openai.azure.com/openai/v1/responses`), statelessly (`store: false`): nothing of a turn is kept by the service. The spending cap is an Azure budget, as for `foundry`.
2. Add the provider's API key as the repository secret `ASSISTANT_API_KEY` ([New repository secret](https://github.com/andreykukalev/motiff-lab/settings/secrets/actions/new)): the Console API key for `anthropic`, the Foundry key for `foundry`, the Azure OpenAI resource's key for `azure-openai`. It reaches the Container App only as the secret `assistant-key`, read as `Assistant:Key`.
3. Add these repository variables ([New repository variable](https://github.com/andreykukalev/motiff-lab/settings/variables/actions/new)). They are identifiers, not secrets:
   - `ASSISTANT_PROVIDER`: `anthropic`, `foundry` or `azure-openai` (`Assistant:Provider`); empty or anything else leaves the assistant "not configured";
   - `ASSISTANT_MODEL` (`Assistant:Model`): for `anthropic` the model id, empty for the default `claude-opus-5-5`; for `foundry` the deployment's name, empty when it was left as `claude-opus-5-5`; for `azure-openai` the deployment's name, required (such as `gpt-6.1-sol`; without it, not configured);
   - `ASSISTANT_ENDPOINT` (`Assistant:Endpoint`): for `foundry` the resource's name or its endpoint `https://<resource>.services.ai.azure.com/anthropic` (without it, not configured); for `azure-openai` the resource's name or its endpoint `https://<resource>.openai.azure.com` (without it, not configured); for `anthropic` leave it empty;
   - `ASSISTANT_ALLOWED`: the object ids of the accounts allowed, comma-separated. Find yours in the [Entra admin center](https://entra.microsoft.com/#view/Microsoft_AAD_UsersAndTenants/UserManagementMenuBlade/~/AllUsers): switch to the `motifflab` tenant, open *Users*, then your account, and copy *Object ID*.
4. Merge the pull request *API deploy: pass the assistant's model settings*. It changes `.github/workflows/api.yml`, so Andrei merges it. With it, the workflow passes the secret and the variables to `api.bicep` (parameters `assistantKey`, `assistantProvider`, `assistantModel`, `assistantEndpoint` and `assistantAllowed`), and the merge deploys the API with them. After a later change of the key or a variable, re-run the [API workflow](https://github.com/andreykukalev/motiff-lab/actions/workflows/api.yml).

Limits are settings of the API: `Assistant:DailyRequests` (200 turns a day for each account by default), `Assistant:TimeoutSeconds` (30 by default) and `Assistant:Effort`, how hard the model thinks (`low`, `medium`, `high`, `xhigh` or `max`; `medium` by default; on `azure-openai` it is the reasoning effort, `max` sent as `xhigh`). On Claude each request caches the instructions and the tools; on `anthropic`, a request the model declines is retried on Anthropic's fallback model (`fallbacks: "default"`), and on `foundry` and `azure-openai` it gets the engine's template answer, as does an answer cut off by the token limit or the content filter. A turn is at most 128 KB, and a question takes at most 8 tool calls. The API logs the outcome, counts, tokens and timing of each turn, never its content. To switch the model off, delete the secret, or empty `ASSISTANT_PROVIDER`, and re-run the workflow.

## Changes later

- Change infrastructure by editing `main.bicep`; the PR shows the `what-if`, and merging applies it.
- The CI identity is Contributor on the resource group, which includes the identity itself. That is acceptable for a single-owner project; move it to its own resource group if more people get write access to the repository.
- To rotate the deployment token: `az staticwebapp secrets reset-api-key --name swa-motifflab --resource-group rg-motifflab`. Nothing else needs to change.
- To add a custom domain, use `Microsoft.Web/staticSites/customDomains` in `main.bicep` after the DNS record exists.
