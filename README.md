# members-area

## Development secrets

Secrets are stored via `dotnet user-secrets` and are never committed to source control.

### AppHost (`AndreGoepel.AppFoundation.AppHost`)

Run from `AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.AppHost/`:

```bash
dotnet user-secrets set "Parameters:database-password" "<your-password>"
```

| Key | Description |
|-----|-------------|
| `Parameters:database-password` | Password for the local Postgres container |

### App (`AndreGoepel.AppFoundation`)

Run from `AndreGoepel.AppFoundation/AndreGoepel.AppFoundation/`:

```bash
dotnet user-secrets set "ConnectionStrings:appfoundation-database" "Host=localhost;Port=59746;Username=db-user;Password=<your-password>;Database=appfoundation-database"
```

| Key | Description |
|-----|-------------|
| `ConnectionStrings:appfoundation-database` | Full connection string for the local Postgres database |