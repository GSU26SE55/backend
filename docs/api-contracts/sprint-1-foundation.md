# Sprint 1 Foundation API Contracts

Auth self-profile endpoints đi chung nhóm auth hiện tại qua gateway: `/api/auth`.
Staff lookup dùng nhóm riêng: `/api/staff`.
Admin staff management dùng nhóm riêng: `/api/admin/staff`.
File endpoints vẫn giữ nhóm file storage đã khai báo riêng.

## Profile

`GET /api/auth/me`

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": null,
  "data": {
    "id": "8a9cf4a7-7e8b-4ed7-9e3b-77b638da90a2",
    "email": "staff@example.com",
    "phoneNumber": "0900123456",
    "fullName": "Nguyen Van A",
    "profile": {
      "accountId": "8a9cf4a7-7e8b-4ed7-9e3b-77b638da90a2",
      "avatarFileId": "1b92d9c8-489a-4a8f-9776-44af7f7b2b7a",
      "externalAvatarUrl": "https://lh3.googleusercontent.com/a/avatar",
      "avatarSource": 1,
      "address": "District 1",
      "birthDate": "1998-04-02T00:00:00Z",
      "timeZone": "Asia/Ho_Chi_Minh"
    },
    "staffProfile": {
      "accountId": "8a9cf4a7-7e8b-4ed7-9e3b-77b638da90a2",
      "employeeCode": "STF-001",
      "department": "Maintenance",
      "maxConcurrentTickets": 4,
      "isAvailable": true,
      "notes": "North area",
      "skills": [
        { "skillCode": "LiFePO4", "skillLevel": 4, "certifiedUntil": "2027-05-01T00:00:00Z" }
      ]
    },
    "displayAvatarUrl": "/api/files/1b92d9c8-489a-4a8f-9776-44af7f7b2b7a/download",
    "roles": ["Staff"]
  }
}
```

`PUT /api/auth/me/profile`

```json
{
  "fullName": "Nguyen Van A",
  "phoneNumber": "0900123456",
  "address": "District 1",
  "birthDate": "1998-04-02T00:00:00Z",
  "timeZone": "Asia/Ho_Chi_Minh"
}
```

`POST /api/auth/me/avatar`

```json
{ "avatarFileId": "1b92d9c8-489a-4a8f-9776-44af7f7b2b7a" }
```

## Files

`POST /api/files/upload` uses `multipart/form-data`.

Fields: `file`, `folderName`, `purpose`.

```json
{
  "isSuccess": true,
  "statusCode": 201,
  "message": "Upload file thành công.",
  "data": {
    "fileId": "1b92d9c8-489a-4a8f-9776-44af7f7b2b7a",
    "objectKey": "avatars/92f7c5e0d6f5479ba83f7e19adf1c5ec.png",
    "fileName": "avatar.png",
    "contentType": "image/png",
    "size": 32544,
    "publicUrl": "http://localhost:9090/solar-battery-files/avatars/92f7c5e0d6f5479ba83f7e19adf1c5ec.png"
  }
}
```

Metadata and access endpoints:

- `GET /api/files/{id}/metadata`
- `GET /api/files/{id}/presigned-url?expiresInMinutes=15`
- `GET /api/files/{id}/download`
- `DELETE /api/files/{id}`

## Staff Assignment

`GET /api/staff?skill=LiFePO4`

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": [
    {
      "accountId": "8a9cf4a7-7e8b-4ed7-9e3b-77b638da90a2",
      "email": "staff@example.com",
      "fullName": "Nguyen Van A",
      "phoneNumber": "0900123456",
      "department": "Maintenance",
      "maxConcurrentTickets": 4,
      "isAvailable": true,
      "displayAvatarUrl": "/api/files/1b92d9c8-489a-4a8f-9776-44af7f7b2b7a/download",
      "skills": [
        { "skillCode": "LiFePO4", "skillLevel": 4, "certifiedUntil": "2027-05-01T00:00:00Z" }
      ]
    }
  ]
}
```

Admin staff endpoints:

- `GET /api/staff/{id}/assignment-profile`
- `PUT /api/admin/staff/{id}/profile`
- `POST /api/admin/staff/{id}/skills`
- `DELETE /api/admin/staff/{id}/skills/{skillCode}`

## Sprint 2-4 Mock Draft

Battery mock routes:

- `GET /api/v1/batteries`
- `GET /api/v1/batteries/{id}`
- `GET /api/v1/batteries/{id}/readings?fromUtc=&toUtc=`
- `POST /api/v1/batteries/{id}/alerts`

Ticket mock routes:

- `GET /api/v1/tickets?status=&priority=&assigneeId=`
- `POST /api/v1/tickets`
- `GET /api/v1/tickets/{id}`
- `POST /api/v1/tickets/{id}/assign`
- `POST /api/v1/tickets/{id}/resolve`
- `POST /api/v1/tickets/{id}/close`

Notification mock routes:

- `GET /api/v1/notifications`
- `POST /api/v1/notifications/test`
- `PUT /api/v1/notifications/{id}/read`
