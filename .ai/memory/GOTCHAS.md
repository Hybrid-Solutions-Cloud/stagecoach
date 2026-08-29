# Gotchas

- `az network bastion rdp` / `--rdp` flags are Windows-client-only; macOS/Linux need `az network bastion tunnel` + their own RDP client.
- Bastion native client needs Standard SKU+ with tunneling enabled; Developer/Basic SKUs cannot do CLI connections.
- Entra RDP to Windows requires the client PC Entra-joined/registered to the same tenant as the VM + AADLoginForWindows extension.
- AAD-issued OpenSSH certs are Linux-only today; Windows targets use `--local-user`.
- Arc SSH needs the HybridConnectivity default endpoint + SSH service config (port 22); the CLI prompts to create it (`--yes` to auto-accept).
- Raw Graph `deviceLocalCredentials` returns Base64 — decode before use.
- PowerShell class types in EXPORTED function signatures (`[OutputType([SomeClass])]`,
  `[SomeClass]$Param`) resolve lazily on first invocation from the CALLER's scope —
  works from inside the module, throws "Unable to find type" for external callers.
  Use string-form `[OutputType('SomeClass')]` and untyped params on public cmdlets.
- `$x = if ($cond) { @() } else { @() }` assigns `$null`, not an empty array —
  wrap the whole conditional: `$x = @(if ($cond) { ... } else { ... })`.
- `return , $array` + `@(...)` at the call site NESTS the array (the comma-wrapped
  array comes through as a single item). Return arrays plainly; let callers `@()`.
