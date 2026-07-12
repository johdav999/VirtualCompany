# Supplier Account Mapping Repair

Use `repair-supplier-account-mapping.ps1` when a supplier default account points to a non-expense account such as `2000`.

List supplier mappings without changing data:

```powershell
.\repair-supplier-account-mapping.ps1 -UseWindowsAuthentication
```

Update one supplier on local SQL Server Express:

```powershell
.\repair-supplier-account-mapping.ps1 -UseWindowsAuthentication -SupplierName "OpenAI" -AccountCode 6540
```

Use the same script after restoring the database in Docker by pointing it at the Docker SQL Server endpoint:

```powershell
$env:VC_SQL_SA_PASSWORD = "YourStrong!Passw0rd"
.\repair-supplier-account-mapping.ps1 -ServerInstance "localhost,1433" -Username sa -Password $env:VC_SQL_SA_PASSWORD -SupplierName "OpenAI" -AccountCode 6540
```

The script only accepts supplier expense accounts from `4000` through `8999`, lists affected suppliers first, and requires either `-SupplierId` or `-SupplierName` before updating.
