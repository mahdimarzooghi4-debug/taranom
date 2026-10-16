using System.Runtime.CompilerServices;

// Only the isolated executable schema-integrity probe can exercise
// internal synthetic PostgreSQL schema initialization.
[assembly: InternalsVisibleTo("Taranom.SupportSchemaAdmission.Probe")]

// TRN-TECH-150: dedicated test-only adversarial response-verifier probe.
[assembly: InternalsVisibleTo("Taranom.AiWorkerBoundary.Probe")]
