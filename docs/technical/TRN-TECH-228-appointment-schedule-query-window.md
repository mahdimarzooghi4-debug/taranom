# TRN-TECH-228 — Appointment Schedule Query Window

## Purpose

Require a bounded temporal query before a future provider schedule adapter can be called, and keep returned source claims within the exact requested interval.

## Contract

- Schedule reads require both `from` and `until`; timestamps must include an explicit UTC marker or numeric offset.
- The query window is represented as instants with `DateTimeOffset`; the application does not infer a local timezone.
- The end instant must be later than the start instant.
- Source observations that contain any slot outside the requested window are rejected as a whole.
- The HTTP contract echoes normalized offsets and retains the existing center, optional department, and patient-selected doctor scope.
- Missing source, source-authentication, availability-verification, booking, clinical-action, and national-release flags remain fail-closed.

## Boundaries

This is a request-shape and scope contract for the future adapter. It does not decide which dates a center offers, a maximum search horizon, local opening-hour rules, capacity, time-zone policy, provider credentials, or booking behavior. No schedule source is configured, so the route still returns an empty claim list.

## Verification

The MemberGovernance unit tests cover invalid windows and out-of-window source claims. The CI-only Kestrel integration checks accepted timestamp normalization and rejects missing, offset-free, and reversed windows.
