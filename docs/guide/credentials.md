# Credential Resolution & Identity

Stagecoach features a multi-tier **Credential Resolver** that eliminates password hunting while preserving zero-persistence security standards.

---

## The Credential Resolution Order

When you select a target in Stagecoach, the resolver evaluates credentials in the following order:

```
[Target Selected]
       │
       ▼
1. Resource Tag Override?
   └─► Checks tag: stagecoach-secret = "https://<vault>.vault.azure.net/secrets/<name>"
       │
       ▼ (if not tagged)
2. Entra Windows LAPS?
   └─► Queries Microsoft Graph /directory/deviceLocalCredentials for rotating cloud LAPS.
       │
       ▼ (if not in LAPS)
3. Active Directory Domain Secret?
   └─► If Domain-Joined, checks Key Vault for: domain-<domainname>-admin
       │
       ▼ (if not a domain secret)
4. Key Vault Per-VM Convention?
   └─► For workgroup machines, checks Key Vault for: vm-<hostname>-localadmin
       │
       ▼ (if none found)
5. UI Drawer Prompt:
   └─► Asks operator once with option: [✔] Save to Key Vault for next time.
```

---

## Handling Domain vs. Workgroup Machines

### 1. Active Directory Domain-Joined Servers
- **Detection:** Azure Resource Graph returns `properties.domainName` (e.g. `CORP.CONTOSO.COM`).
- **Target User:** Injected as `CORP\Administrator` or the operator's configured domain user.
- **Benefit:** One domain credential or active session covers all servers in the entire domain.

### 2. Standalone Workgroup Servers
- **Detection:** Azure Resource Graph returns `properties.domainName == 'WORKGROUP'` or empty.
- **Target User:** Injected as `.\Administrator` or `.\localadmin`.
- **Secret Lookup:** Looked up individually per machine in Azure Key Vault (`vm-<name>-localadmin`).

---

## Security & RBAC Enforcement

- **Operator RBAC Only:** Stagecoach does not use a shared service principal or master password. All reads to LAPS and Key Vault execute with the **signed-in operator's own Azure token**.
- **Audit Trails:** Every password lookup is recorded in the Azure Key Vault and Entra audit logs under the operator's UPN.
- **Zero Disk Persistence:** No passwords or secrets are ever saved to local files, config files, or browser storage.

