# Firebase Cloud Messaging

## Firebase Admin configuration

The backend initializes the Firebase Admin SDK once during application startup.
Credential lookup order is:

1. `Firebase:ServiceAccountJson`
2. `Firebase:ServiceAccountPath`
3. `GOOGLE_APPLICATION_CREDENTIALS`

No Google ID token, OAuth client ID, or Google Login credential is used for FCM.
When none of the credential sources is configured, the application still starts
and logs a clear warning. Notification history is retained with a failed delivery
status until Firebase is configured.

Development example:

```json
{
  "Firebase": {
    "ProjectId": "coldchainx-project-id",
    "ServiceAccountPath": "/absolute/path/to/firebase-service-account.json",
    "ServiceAccountJson": ""
  }
}
```

Production environment variables (choose one credential method):

```bash
Firebase__ProjectId=coldchainx-project-id
Firebase__ServiceAccountJson='{"type":"service_account",...}'
```

or:

```bash
Firebase__ProjectId=coldchainx-project-id
GOOGLE_APPLICATION_CREDENTIALS=/run/secrets/firebase-service-account.json
```

To obtain the credential, open Firebase Console, select the project, then go to
Project settings > Service accounts > Firebase Admin SDK and generate a private
key. Store the JSON outside the repository. Common service-account filenames are
ignored by `.gitignore`.

For iOS, upload the APNs authentication key or certificate in Firebase Console.
For Android, the mobile app must create the
`coldchainx_operational` notification channel because Android channels are
created client-side.

## Database migration

Migration: `AddFirebaseNotifications`

```bash
dotnet ef migrations add AddFirebaseNotifications \
  --project ColdChainX.Infrastructure \
  --startup-project ColdChainX.API

dotnet ef database update \
  --project ColdChainX.Infrastructure \
  --startup-project ColdChainX.API
```

The migration creates `device_tokens`, extends `notifications`, adds the global
unique token index, foreign keys, and user/read/type/time indexes. It does not
delete or recreate existing production tables.

## Mobile API examples

All endpoints require the application JWT in
`Authorization: Bearer <application-jwt>`.

Register a token:

```http
POST /api/notifications/register-token
Content-Type: application/json

{
  "deviceToken": "FCM_DEVICE_TOKEN",
  "platform": "Android",
  "deviceId": "DEVICE_IDENTIFIER",
  "deviceName": "Samsung Galaxy S24",
  "appVersion": "1.0.0"
}
```

Unregister a token:

```http
DELETE /api/notifications/unregister-token
Content-Type: application/json

{
  "deviceToken": "FCM_DEVICE_TOKEN"
}
```

Read notification history:

```http
GET /api/notifications?pageNumber=1&pageSize=20&isRead=false&type=TRIP_ASSIGNED
GET /api/notifications/unread-count
PUT /api/notifications/NOTIFICATION_ID/read
PUT /api/notifications/read-all
```

Admin test send (authenticated users are also allowed in Development):

```http
POST /api/notifications/test
Content-Type: application/json

{
  "userId": "USER_ID",
  "title": "Firebase test",
  "body": "Test notification from ColdChainX backend",
  "type": "TEST",
  "referenceId": null
}
```

## Firebase payload

Messages include both notification and data payloads. Data values are always
strings and contain routing identifiers only:

```json
{
  "notification": {
    "title": "Bạn có chuyến mới",
    "body": "Bạn vừa được phân công một chuyến vận chuyển mới."
  },
  "data": {
    "type": "TRIP_ASSIGNED",
    "referenceId": "TRIP_ID",
    "tripId": "TRIP_ID",
    "screen": "trip-detail"
  },
  "android": {
    "priority": "high",
    "notification": {
      "channelId": "coldchainx_operational",
      "sound": "default"
    }
  },
  "apns": {
    "aps": {
      "sound": "default",
      "contentAvailable": true
    }
  }
}
```

The mobile app must use this data only for navigation and load protected resource
details from an authorized backend API.

## Manual test with a real device

1. Apply `AddFirebaseNotifications`.
2. Configure the Firebase service account and restart the API. Confirm the
   startup log reports that FCM initialized without printing credentials.
3. Sign in to the mobile app and obtain its FCM registration token.
4. Call `POST /api/notifications/register-token` with the application JWT.
5. As an Admin, call `POST /api/notifications/test` for that user.
6. Confirm the push arrives in foreground/background according to the mobile
   app's notification handlers.
7. Confirm `GET /api/notifications` contains the history row and its delivery
   status is `SENT` or `PARTIALLY_SENT`.
8. Uninstall the app or invalidate the token, send again, and confirm an
   unregistered/invalid token is deactivated. Temporary Firebase failures must
   leave the token active.

## Business events

Implemented after the corresponding business data is saved:

- `TRIP_ASSIGNED`
- `TRIP_DELAYED`
- `INCIDENT_CREATED`
- `RESCUE_ASSIGNED`
- `EXPENSE_APPROVED`
- `ORDER_UPDATED`

`EXPENSE_REJECTED` is not connected because the current incident expense workflow
has no reject operation or rejected status. Adding a guessed recipient or status
transition would change the existing business model.

Firebase sends are currently synchronous and best-effort. Firebase failures do
not roll back successful core operations. For stronger production delivery
guarantees, add an outbox/background worker in a separate change.
