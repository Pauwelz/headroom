# Telemetry stays in the company's subscription

Each Instance sends its telemetry with OpenTelemetry to an Application Insights resource in the company's own subscription, or to an OTLP backend that its Operator controls. Only its Operator reads it. Headroom never sends telemetry to us, and the telemetry holds no personal data: no names, emails, Login codes or Person ids, and only aggregate counts of feature use. An Operator who wants our help shares a Workbook view or a query result in a GitHub issue.

## Considered Options

- **Phone-home to an Application Insights resource that we host:** it gives us data to improve Headroom. But every Instance then depends on us (ADR 0003), we become a processor of company data under GDPR, and each company must consent.
- **Opt-in phone-home of anonymous counts:** less risk, but it still needs an endpoint that we host and keep up.
- **Usage per Person for Planners:** it is close to staff monitoring in a company of 50 people, and it puts personal data in telemetry.

## Consequences

- The app exports to Azure Monitor when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
- The app exports to OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` or the endpoint of one signal (`OTEL_EXPORTER_OTLP_TRACES_ENDPOINT`, `_METRICS_ENDPOINT` or `_LOGS_ENDPOINT`) is set.
- Both exports can be on at the same time. With neither set, Headroom exports nothing and runs as before.
- Personal data stays out because we do not collect it, not because a processor redacts it. An integration test asserts that no email, name or Login code reaches an exported span or log.
- Sampling is off, so every error is kept. If volume grows, tail-based sampling in an OpenTelemetry Collector can keep all errors.
