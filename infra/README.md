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
| [`services-bootstrap.sh`](services-bootstrap.sh) | One-time, in the subscription with `rg-shared`: the group `rg-motifflab` with its CI identity, Motiff Lab's database in the shared free-tier Cosmos DB account, and the API's identity with access to that database only. |
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

The scan server (`services/omr`) is published and deployed by [`.github/workflows/scan-server.yml`](../.github/workflows/scan-server.yml) whenever `services/omr` or `scan.bicep` changes on `main`, or when run by hand from the Actions tab. It builds the image (about 2 GB, longer than CI's 5 minutes, hence its own workflow), pushes it to `ghcr.io/andreykukalev/motiff-omr`, and deploys `scan.bicep`; the job summary shows the server's address. The API's [`.github/workflows/api.yml`](../.github/workflows/api.yml) does the same for `src/MotiffLab.Api` and `api.bicep`, once sign-in is set up (below). Until the setup has run, both only publish their images.

Nothing here has a standing cost: the Consumption-only environment has no load balancer and no Log Analytics workspace, the apps scale to zero, and the database stays within the free tier.

Once, signed in to the subscription with `rg-shared`, and to GitHub:

```bash
az login
az account set --subscription "<subscription with rg-shared>"
gh auth login
./infra/services-bootstrap.sh
```

The script finds the free-tier account in `rg-shared`, adds up the RU/s its databases already use, and stops without creating anything if Motiff Lab's 400 RU/s would go past the free 1000. Otherwise it asks, then creates `rg-motifflab` with a CI identity ([`bootstrap.bicep`](bootstrap.bicep), Contributor on that group only), the database `motifflab` with the container `projects`, the identity `id-motifflab-api` with data access to that database only, and the repository variables `AZURE_SERVICES_*` and `COSMOS_ENDPOINT`. It does not change the Cosmos DB account, its other databases or any other group. Re-running it is safe. Then run the `Scan server` workflow from the Actions tab.

GitHub makes a new package private, and Container Apps pulls without credentials. If a workflow stops with *the package is private*, open your profile's **Packages**, the package, **Package settings**, **Change visibility**, **Public**, and re-run it.

## Sign-in (Microsoft Entra External ID)

Players sign in with Apple, Google or an email code before their projects are saved online or a page goes to the hosted scan server (decision record [0014](../docs/decisions/0014-hosted-scan-server.md)). External ID is free for the first 50,000 monthly active users. Set it up once in the [Microsoft Entra admin center](https://entra.microsoft.com), signed in with the account that owns the subscription:

1. **Create the tenant.** *Entra ID → Overview → Manage tenants → Create → External*. Name it *Motiff Lab*, pick a domain such as `motifflab` (sign-in then runs on `motifflab.ciamlogin.com`), choose Europe, and link it to the subscription and group of the services, `rg-motifflab`. Switch to the new tenant for the steps below and note its **tenant id**.
2. **Register the API.** *App registrations → New registration*: *Motiff Lab API*, this tenant only, no redirect URI. Under *Expose an API*, accept the Application ID URI `api://<client id>` and add two scopes, both consentable by admins and users: `Projects.ReadWrite` (*Save and read your projects*) and `Scan` (*Read printed music and recordings you send*). In *Manifest*, set `"requestedAccessTokenVersion": 2`. Note the **client id**.
3. **Register the web app.** *New registration*: *Motiff Lab*, this tenant only, platform *Single-page application*, redirect URIs `https://<the Static Web App's host>/authentication/login-callback`, `https://localhost:7281/authentication/login-callback` and `http://localhost:5112/authentication/login-callback` (Entra takes no wildcards, so a pull request preview signs in only once its address is added here). Under *API permissions*, add *My APIs → Motiff Lab API →* both scopes, then *Grant admin consent*. Note this **client id** too; the iOS app gets its own registration later.
4. **Create the user flow.** *External Identities → User flows → New user flow*: *Sign up and sign in*, with *Email one-time passcode*, collecting no attributes beyond the email. Under the flow's *Applications*, add *Motiff Lab*.
5. **Add Google and Apple** as identity providers and tick them in the user flow: [Google](https://learn.microsoft.com/entra/external-id/customers/how-to-google-federation-customers) needs an OAuth client in Google Cloud; [Apple](https://learn.microsoft.com/entra/external-id/customers/how-to-apple-federation-customers) needs the Apple Developer Program, and its key must be renewed every 6 months.
6. **Tell GitHub**, as repository variables (identifiers, not secrets; `services-bootstrap.sh` has set the others):

   ```bash
   gh variable set AUTH_AUTHORITY --body "https://<domain>.ciamlogin.com/<tenant id>/v2.0"
   gh variable set AUTH_AUDIENCE --body "<Motiff Lab API client id>"
   ```

   Then run the `API` workflow from the Actions tab (and make the `motiff-api` package public, as for `motiff-omr`), and re-run `Scan server`: from then on it reads pages only for signed-in players.
7. **Tell the web app**, in [`src/MotiffLab.Web/wwwroot/appsettings.json`](../src/MotiffLab.Web/wwwroot/appsettings.json) (public identifiers, committed): `Authority` `https://<domain>.ciamlogin.com/<tenant id>`, `ClientId` the *Motiff Lab* client id, `ApiScope` `api://<API client id>/Projects.ReadWrite`, `ScanScope` `api://<API client id>/Scan`, and the addresses the `API` and `Scan server` workflows print, as `Api` and `Scan`. Until these are set, the app shows nothing about accounts.

## Changes later

- Change infrastructure by editing `main.bicep`; the PR shows the `what-if`, and merging applies it.
- The CI identity is Contributor on the resource group, which includes the identity itself. That is acceptable for a single-owner project; move it to its own resource group if more people get write access to the repository.
- To rotate the deployment token: `az staticwebapp secrets reset-api-key --name swa-motifflab --resource-group rg-motifflab`. Nothing else needs to change.
- To add a custom domain, use `Microsoft.Web/staticSites/customDomains` in `main.bicep` after the DNS record exists.
