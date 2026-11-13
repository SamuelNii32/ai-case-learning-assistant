# Disposable Auth0 users for isolated staging

CasePilot includes a trusted command-line bootstrap for one disposable student
and one disposable instructor. It is not an HTTP endpoint and is never invoked
during normal startup. It does not match by email, accept a local user ID or
role, or modify an existing mapped account.

## Safety gates

The command refuses to run unless all of these conditions are true:

- `--bootstrap-staging-auth0-test-users` is passed explicitly;
- `STAGING_AUTH0_BOOTSTRAP_ENABLED=true`;
- `CASEPILOT_DEPLOYMENT_ENVIRONMENT=staging`;
- `STAGING_AUTH0_BOOTSTRAP_CONFIRMATION` exactly equals
  `CASEPILOT-STAGING-DISPOSABLE-USERS`;
- the configured database provider is PostgreSQL;
- the PostgreSQL database name contains `staging`, `stage`, `test`, or
  `sandbox` as a separate name segment;
- student and instructor Auth0 subjects are present and different.

The local IDs and `.invalid` email addresses are deterministically derived
from each validated Auth0 subject. Roles are fixed by the server: the student
is never an instructor and the instructor is always an instructor. Existing
identity constraints and `UserIdentityRepository` conflict handling remain the
authority.

## Operator procedure

1. Create two disposable users in the staging Auth0 application. Do not reuse a
   production identity.
2. Verify the deployed application points only at the isolated staging
   PostgreSQL database. Check the database name again before continuing.
3. Open an authenticated App Service Kudu/SSH console. Set the following only
   in that console process; do not add the subjects or confirmation to source,
   GitHub, screenshots, or deployment logs:

   ```powershell
   $env:STAGING_AUTH0_BOOTSTRAP_ENABLED = 'true'
   $env:CASEPILOT_DEPLOYMENT_ENVIRONMENT = 'staging'
   $env:STAGING_AUTH0_BOOTSTRAP_CONFIRMATION = 'CASEPILOT-STAGING-DISPOSABLE-USERS'
   $env:STAGING_AUTH0_STUDENT_SUBJECT = '<disposable-student-subject>'
   $env:STAGING_AUTH0_INSTRUCTOR_SUBJECT = '<disposable-instructor-subject>'
   dotnet .\IngestionApi.dll --bootstrap-staging-auth0-test-users
   ```

4. Record only the generated local user IDs and statuses. `CreatedAndLinked`
   is expected on the first run and `AlreadyLinked` on an identical repeat.
   A conflict or collision is a stop condition; do not alter an existing row to
   force the command to succeed.
5. Clear the console variables immediately:

   ```powershell
   Remove-Item Env:STAGING_AUTH0_BOOTSTRAP_ENABLED
   Remove-Item Env:STAGING_AUTH0_BOOTSTRAP_CONFIRMATION
   Remove-Item Env:STAGING_AUTH0_STUDENT_SUBJECT
   Remove-Item Env:STAGING_AUTH0_INSTRUCTOR_SUBJECT
   ```

`CASEPILOT_DEPLOYMENT_ENVIRONMENT=staging` may remain as a non-secret staging
marker. Production must use `production`.

## Cleanup

The preferred cleanup is deletion of the entire isolated staging database and
the two disposable Auth0 users after testing. This avoids unsafe partial row
deletion once uploads, classes, sessions, notes, or identity relationships have
been created. Never point this command or cleanup work at the production
database.
