# Releases

Stagecoach has not yet published a supported production release. The current native-Windows implementation is packaged as version `0.1.0` for administrator validation.

Release artifacts contain a self-contained `win-x64` ZIP, SHA-256 checksum, and per-machine MSI. Local encrypted state under `%LOCALAPPDATA%\Stagecoach` is intentionally not removed during an application upgrade or uninstall.
