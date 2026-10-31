# TRN-TECH-230 — Strict Offset Timestamp Syntax

## Purpose

Accept only a clear, culture-independent timestamp form at the schedule-read HTTP boundary.

## Contract

- Query timestamps use `YYYY-MM-DDTHH:mm:ss[.fraction](Z|±HH:mm)`.
- A fractional part is optional and, when present, contains one to seven decimal digits.
- The parser accepts only ASCII digits, uppercase `T`/`Z`, and an explicit offset.
- Calendar and offset validity are checked by `DateTimeOffset`; the application never supplies a local timezone.
- Numeric offsets in URLs must be percent-encoded by the client.
- Free-form dates, space separators, offset-free values, impossible calendar dates, and unsupported precision are rejected.

## Boundaries

This defines wire syntax only. It does not select a center timezone, opening hours, booking window, or schedule policy. The source remains unconfigured and no claim becomes verified availability.

## Verification

The synthetic Kestrel integration uses a percent-encoded numeric offset and rejects free-form, offset-free, impossible-date, and over-precision inputs.
