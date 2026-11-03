# OData read API

All list reads are served by an OData v4 service at `/odata`. The older `GET /api/v1/{postings,companies,applications,notes,criteria,search-runs}` list endpoints were retired in feature 006 (ADR 0028). Detail reads (`GET /api/v1/{entity}/{id}`) and every write stay on `/api/v1`.

The service is read-only and scoped to the authenticated owner: you only ever see your own rows.

## Conventions used below

```bash
export BASE=https://roleradar.example   # the API origin
export TOKEN=<access token>             # a bearer token issued for the API
```

Every example sends `Authorization: Bearer $TOKEN`. URLs are in double quotes with `\$` so the shell keeps the OData `$` option names, and `%20` for spaces. The same URLs work from any HTTP client.

Entity sets: `Postings`, `Companies`, `Applications`, `Notes`, `Criteria`, `SearchRuns`. The machine-readable model is `GET $BASE/odata/$metadata`; the committed copy is `contracts/odata/metadata.xml`.

## Rules

- Supported query options: `$filter`, `$orderby`, `$select`, `$count`, `$top`, `$skip`. Everything else (`$expand`, `$search`, `$apply`, ...) is rejected.
- `$top`: default **25**, maximum **100**. `$skip` pages through the rest.
- `$filter`: at most **100** expression nodes and nesting depth **10**. Allowed operators: `eq ne gt ge lt le and or not`. Allowed functions: `tolower contains startswith endswith`. No arithmetic, no `any`/`all`.
- String search must be lowercase on both sides: wrap the property in `tolower()` and give a lowercase literal, for example `contains(tolower(title),'engineer')`. A bare `contains(title,'x')`, or a literal with capitals, is rejected with 400.
- `$orderby` accepts only the fields listed per set below, at most 5 of them. Rows without a value for a nullable field (`score`, `firstResponseAt`, `lastEventAt`, `effectiveTo`) sort last in both directions.
- Without `$orderby` the order is stable: companies and criteria by name, notes by `updatedAt` descending, search runs by `runDate` descending, postings and applications by `id`.
- Responses use the standard OData envelope: `{ "value": [ ... ] }`, plus `"@odata.count": <n>` when `$count=true`. The count is the number of rows matching `$filter`, ignoring `$top` and `$skip`.
- Notes return metadata only. The note body (`content`, up to 8 MiB) is never in a list response unless you name it in `$select`, and it cannot be filtered or sorted on.
- Property names are camelCase and case-sensitive.

## Errors

- `400` for a rejected query (unknown or disallowed field, `$top` above 100, uppercase search term, unsupported operator, too many nodes). The body is the API error envelope: `{ "ok": false, "errorCode": "validation_error", "message": "<what was rejected>" }`.
- `401` without a valid bearer token, `403` when the token's subject is not the configured owner.
- `405` with `Allow: GET` for `POST`, `PUT`, `PATCH` and `DELETE` on any entity set. There are no write or delete operations in OData.

```bash
# top above the limit -> 400 validation_error
curl -s -i -H "Authorization: Bearer $TOKEN" "$BASE/odata/Postings?\$top=101"

# uppercase search term -> 400 validation_error
curl -s -i -H "Authorization: Bearer $TOKEN" "$BASE/odata/Postings?\$filter=contains(tolower(title),'Engineer')"

# write verb -> 405, Allow: GET
curl -s -i -X DELETE -H "Authorization: Bearer $TOKEN" "$BASE/odata/Postings"
```

## Postings

Orderable: `title`, `companyName`, `location`, `disposition`, `resurfacePolicy`, `score`, `createdAt`, `updatedAt`, `id`. All properties are filterable.

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Postings?\$filter=contains(tolower(title),'engineer')%20and%20disposition%20eq%20'applied'"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Postings?\$orderby=score%20desc,title%20asc"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Postings?\$select=id,title,companyName,score"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Postings?\$count=true&\$top=5"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Postings?\$top=10&\$skip=20"
```

## Companies

Orderable: `name`, `status`, `ownershipType`. Filterable: `name`, `status`, `ownershipType`, `headquarters` (`description` and `categoryTags` are not).

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Companies?\$filter=startswith(tolower(name),'ac')"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Companies?\$orderby=name%20asc"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Companies?\$select=id,name,status"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Companies?\$count=true&\$top=5"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Companies?\$top=10&\$skip=10"
```

## Applications

Orderable: `postingTitle`, `companyName`, `status`, `appliedAt`, `hasInterview`, `firstResponseAt`, `lastEventAt`, `ageInDays`, `id`. Filterable: everything except `ageInDays`.

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Applications?\$filter=hasInterview%20eq%20true%20and%20appliedAt%20ge%202026-01-01T00:00:00Z"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Applications?\$orderby=lastEventAt%20desc"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Applications?\$select=id,postingTitle,companyName,status,appliedAt"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Applications?\$count=true&\$top=5"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Applications?\$top=25&\$skip=25"
```

## Notes

Orderable: `heading`, `createdAt`, `updatedAt`. Filterable: `heading`, `linkedEntityType`, `createdAt`, `updatedAt` (`content`, `tags` and `linkedEntityId` are not).

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Notes?\$filter=contains(tolower(heading),'interview')"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Notes?\$orderby=heading%20asc"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Notes?\$select=id,heading,updatedAt"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Notes?\$count=true&\$top=5"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Notes?\$top=10&\$skip=10"

# the body is returned only when it is selected explicitly
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Notes?\$select=id,heading,content&\$top=1"
```

## Criteria

Orderable: `name`, `category`, `effectiveFrom`, `effectiveTo`, `isActive`. Filterable: all except `ruleText` (which is returned but not filterable).

`effectiveTo` is exclusive: a criterion is in effect on date `D` when `effectiveFrom le D` and (`effectiveTo` is null or `D lt effectiveTo`). A criterion without `effectiveTo` is open-ended.

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Criteria?\$filter=effectiveFrom%20le%202026-06-01T00:00:00Z%20and%20(effectiveTo%20eq%20null%20or%20effectiveTo%20gt%202026-06-01T00:00:00Z)"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Criteria?\$orderby=effectiveTo%20asc"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Criteria?\$select=id,name,category"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Criteria?\$count=true&\$top=5"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/Criteria?\$top=10&\$skip=10"
```

## SearchRuns

Orderable: `runDate`, `newPostings`. Filterable: `runDate`, `newPostings` (`summary` is returned but not filterable).

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/SearchRuns?\$filter=newPostings%20gt%200"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/SearchRuns?\$orderby=newPostings%20desc"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/SearchRuns?\$select=id,runDate,newPostings"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/SearchRuns?\$count=true&\$top=5"
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/odata/SearchRuns?\$top=10&\$skip=10"
```
