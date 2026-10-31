# TRN-TECH-233 — Strict Timestamp Syntax for Schedule Provenance

## Purpose

Apply the same bounded timestamp syntax to schedule observations captured with a synthetic appointment request as to the schedule-read query.

## Accepted timestamp form

The observation time, slot start, and slot end must use a full date and time with seconds and an explicit UTC marker or numeric offset. Fractional seconds are optional and may contain one to seven digits. Invalid calendar dates, missing offsets, whitespace separators, and excess fractional precision are rejected before command deserialization.

The parser is shared with the schedule-read HTTP boundary so both contracts enforce one syntax rule. Semantic validation remains separate: the selected slot must match the request slot, and the end must follow the start.

## Safety boundary

Timestamps remain caller-supplied synthetic claims. Parsing their syntax does not authenticate a source, prove freshness or availability, reserve a slot, confirm a booking, or authorize clinical action. The member demo still sends null because no schedule source is connected.

## Verification

The HTTP/PostgreSQL integration test retains and reads valid metadata, and rejects missing offsets, non-ISO separators, impossible calendar dates, and fractional precision beyond seven digits. Existing schedule query timestamp tests continue to use the shared parser.

Only the synthetic appointment-request write/read response advances to TRN-TECH-233.
