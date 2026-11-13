# Azure information checklist for CasePilot staging

This checklist is for the person who owns the Azure subscription. For this
step, **do not change, disconnect, restart, scale, deploy, or reveal anything**.
We only need screenshots and a few written answers so we can design a safe
staging test without affecting the current application.

## Before taking screenshots

- Keep passwords, connection strings, API keys, client secrets, access keys,
  publish-profile credentials, and database credentials hidden.
- Do not click **Show value**, **Reveal**, **Download publish profile**,
  **Disconnect**, **Save**, **Restart**, **Swap**, or **Deploy**.
- It is safe to show resource names, regions, hostnames, plan tiers, setting
  names, managed-identity status, and role names.
- If a page unexpectedly displays a secret value, close or redact that part
  before sharing the screenshot.

## Screenshots we need

Open the CasePilot **App Service** in the Azure portal and send these images.
Portal labels sometimes move; use the closest matching page if a label differs.

1. **`01-app-overview.png` — App Service > Overview**
   - Include app name, status, location, default domain, operating system, and
     the App Service Plan name.
   - The subscription ID may be cropped or covered.

2. **`02-plan-size.png` — App Service Plan > Overview or Scale up**
   - Include the current pricing tier/SKU and instance size.
   - Do not select or apply a different tier.

3. **`03-runtime-settings.png` — App Service > Settings > Configuration > General settings**
   - Include stack/runtime version, platform bitness, HTTP version, Web Sockets,
     Always On, HTTPS Only, and minimum inbound TLS version where shown.

4. **`04-deployment-center-settings.png` — App Service > Deployment Center > Settings**
   - Include source, build provider, organization, repository, and branch.
   - If the page says it is disconnected, include that message.
   - Do **not** click Disconnect or Save yet.

5. **`05-deployment-center-log.png` — App Service > Deployment Center > Logs**
   - Include the latest deployment date, status, source, and commit identifier.
   - Do not open a log section if it displays credentials.

6. **`06-deployment-slots.png` — App Service > Deployment > Deployment slots**
   - Include the list of slots. If Azure shows an upgrade message instead,
     capture that message.
   - If a staging slot exists, also capture whether Auto Swap is enabled.

7. **`07-managed-identity.png` — App Service > Settings > Identity**
   - Include whether the system-assigned identity is On or Off and the names of
     any user-assigned identities.
   - The object/principal ID is not a password, but it may still be covered if
     preferred.

8. **`08-environment-variable-names.png` — App Service > Settings > Environment variables**
   - Show **names only** and whether each is marked as a deployment-slot setting.
   - Keep every value hidden. Multiple screenshots are fine.
   - We especially need to know whether names beginning with `AUTH0_`, `BFF_`,
     `DATABASE_`, `POSTGRES_`, `AZURE_STORAGE_`, `DATA_PROTECTION_`, `JWT_`,
     `OPENAI_`, and `OPENROUTER_` exist. Do not send their values.

9. **`09-network-and-domains.png` — App Service > Networking and Custom domains**
   - Include public network access, inbound access restrictions, private
     endpoints if any, the default Azure hostname, and any verified custom
     domain names.

10. **`10-seven-day-metrics.png` — App Service > Monitoring > Metrics**
    - Use the last seven days and show CPU percentage, memory working set,
      average response time, requests, and HTTP queue length if available.
    - One screenshot per graph is acceptable. No load test is required.

## Written answers we need

Please answer these without sending credentials:

- Is the API the only App Service, or is there a separate worker/WebJob?
- If there is a worker, what Azure resource type hosts it, and is it running?
- Do the API and worker use the same PostgreSQL database and the same Blob
  container? Answer yes, no, or unknown; do not send connection strings.
- Is there already a staging App Service or staging deployment slot?
- Is there a separate test database and test Blob container we may use?
- Who can administer the Auth0 application? Do not send Auth0 credentials.
- Who currently deploys the application, and is deployment manual or automatic?
- Is there a maintenance window or backup/rollback procedure already in use?

## What we will decide after receiving this information

We will determine whether the existing plan supports a staging slot, whether a
merge could trigger Azure automatically, whether the app has enough spare
capacity, and which isolated database, Blob container, Auth0 application, and
Data Protection key ring staging should use.

No staging deployment, production change, paid upgrade, Auth0 change, database
change, or DNS change should happen until those decisions are reviewed and
explicitly approved.
