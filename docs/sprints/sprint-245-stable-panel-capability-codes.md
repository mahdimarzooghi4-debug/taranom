# Sprint 245 — Stable panel capability tokens

## Goal

Make the v1 panel-grant wire values independent of C# enum member names, preventing a source refactor from silently changing the claim contract.

## Scope delivered

- Adds an explicit, bidirectional mapping between the six owner-confirmed capabilities and their existing v1 claim tokens.
- Keeps every current token spelling and the v1 grant format unchanged.
- Rejects undefined capabilities and unknown or noncanonical token strings.
- Adds tests pinning each stable token and verifying unknown values fail closed.

## Boundaries

This change defines claim encoding only; it does not issue grants, assign capabilities to people, define roles, or authenticate identities. The policy remains unwired to an identity provider, issuer, resource resolver, route, or production panel.

No real health, member, center, insurance, or financial data is used. CI is not human code review or formal QA.
