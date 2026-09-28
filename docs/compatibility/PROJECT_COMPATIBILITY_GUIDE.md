# Project Compatibility Guide

This WPF shell should stay stable. Customer-specific behavior should be added as configuration, profiles, or adapters.

## Compatibility Units

- MES plugin: route checks, logins, result uploads, and MES-specific response parsing.
- Export plugin: CSV, DCL, TXT, and customer-specific report formats.
- Protocol adapter: lower-machine commands and payload parsing.
- Project profile: default customer configuration for one product or line.
- Third-party libraries: vendor DLLs placed under `CH_V03.Plugins/libs`, not under old WinForms `bin` paths.

## New Project Checklist

For each legacy WinForms project, capture these items before coding:

- Customer, product, station, test type, test program.
- Scanner connection type, port, baud rate, and trigger command.
- Lower-machine connection type, port, baud rate, OK command, NG command, item request command, start signal, ignored signals.
- Barcode rules: length, suffix, NoRead behavior, auto/manual/disabled scan mode.
- Test item payload sample and expected parsed fields.
- Test result payload sample and expected PASS/FAIL result.
- MES pre-check endpoint, request method, field names, success response rule, failure response rule.
- MES upload endpoint, request method, field names, test item JSON shape, success response rule, failure response rule.
- CSV/DCL/TXT output path, file name rule, encoding, header, append/overwrite behavior.
- Yield control rule: threshold, denominator, whether upload is blocked.
- Runtime logs needed by onsite debugging.

## Profile Convention

Put project profiles under `CH_V03.Plugins/profiles/*.json`.

Use `docs/compatibility/PROJECT_PROFILE_TEMPLATE_CN.md` as the default field reference when creating a new customer profile.

Each profile should include:

- `Id`
- `Name`
- `CustomerName`
- `ProductName`
- `ProtocolAdapterId`
- `Configuration`

The `Configuration` object uses the same shape as `config/appsettings.json`.

## Protocol Adapter Convention

Create a new `ITestProtocolAdapter` implementation when a customer changes:

- lower-machine OK/NG commands
- start signal
- ignored heartbeat/status payload
- item list payload format
- result payload format

Avoid adding customer-specific parsing branches directly to `MainWindowViewModel` or `TestFlowService`.
